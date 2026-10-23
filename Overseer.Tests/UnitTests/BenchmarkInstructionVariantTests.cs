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
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// The instruction variant catalog: what makes a variant folder valid, how a variant that extends
/// another resolves, the content hash and run stamp, and that the production tool-guide hash never
/// reads a variant folder.
/// </summary>
public sealed class BenchmarkInstructionVariantTests : IDisposable
{
    private readonly string _root;

    public BenchmarkInstructionVariantTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "BenchmarkInstructionVariantTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch { }
        }
    }

    private sealed class FakeToolHandler : IToolHandler
    {
        public FakeToolHandler(string toolName, bool isExperimental = false)
        {
            ToolName = toolName;
            IsExperimental = isExperimental;
        }

        public string ToolName { get; }
        public string Description { get; set; } = "A fake tool.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;
        public bool IsExperimental { get; }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true, Content = "ok" });
    }

    private static readonly IToolHandler[] Handlers =
    {
        new FakeToolHandler("wiki_search"),
        new FakeToolHandler("get_item_stats"),
        new FakeToolHandler("get_monster_stats"),
        new FakeToolHandler("get_artifact_stats"),
        new FakeToolHandler("get_character_info", isExperimental: true)
    };

    /// <summary>Writes a variant folder: its <c>variant.json</c> (when given) and the named files.</summary>
    private string WriteVariant(string folder, string? variantJson, params (string Name, string Content)[] files)
    {
        string path = Path.Combine(_root, folder);
        Directory.CreateDirectory(path);
        if (variantJson != null)
        {
            File.WriteAllText(Path.Combine(path, BenchmarkInstructionVariantCatalog.VariantFileName), variantJson);
        }
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(path, name), content);
        }
        return path;
    }

    private BenchmarkInstructionVariantCatalog Catalog(IEnumerable<IToolHandler>? handlers = null)
        => new(_root, handlers ?? Handlers, NullLogger.Instance);

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // -- A valid variant -------------------------------------------------------------------------

    [Fact]
    public void AValidVariant_ResolvesItsAppendixGuidesToolsAndExperiments()
    {
        string folder = WriteVariant("source-probe", """
            {
              "name": "source-probe",
              "description": "A probe.",
              "promptAppendix": "appendix.md",
              "toolGuides": { "get_item_stats": "items.md", "get_character_info": "character.md" },
              "experimentalTools": ["get_character_info"],
              "experiments": ["stats_referenced_by"]
            }
            """,
            ("appendix.md", "\n  Read the character first.  \n"),
            ("items.md", "Item guide text."),
            ("character.md", "Character guide text."));

        var variant = Catalog().Find("source-probe");

        Assert.NotNull(variant);
        Assert.True(variant!.IsValid, variant.InvalidReason);
        Assert.Equal("A probe.", variant.Description);
        var context = variant.Context!;
        Assert.Equal("source-probe", context.Name);
        Assert.Equal("Read the character first.", context.Appendix);
        Assert.Equal("Item guide text.", context.ToolGuides["get_item_stats"]);
        Assert.Equal("Character guide text.", context.ToolGuides["GET_CHARACTER_INFO"]);
        Assert.Equal(new[] { "get_character_info" }, context.ExperimentalTools);
        Assert.True(context.HasExperiment(BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment));
        Assert.False(context.HasExperiment("something_else"));

        Assert.Equal(BenchmarkService.ComputeToolGuidesSha256(folder), context.Hash);
        Assert.Equal(64, context.Hash.Length);
        Assert.Equal("source-probe:" + context.Hash[..12], context.Stamp);
    }

    [Fact]
    public void AVariantWithOnlyAName_IsValid_AndHasNoAppendix()
    {
        WriteVariant("bare", "{ \"name\": \"bare\" }");

        var variant = Catalog().Find("bare")!;

        Assert.True(variant.IsValid, variant.InvalidReason);
        Assert.Null(variant.Context!.Appendix);
        Assert.Empty(variant.Context.ToolGuides);
        Assert.Empty(variant.Context.ExperimentalTools);
        Assert.Empty(variant.Context.Experiments);
    }

    [Fact]
    public void TheCatalog_ListsEveryFolderByName_AndFindsNothingForAnUnknownName()
    {
        WriteVariant("zeta", "{ \"name\": \"zeta\" }");
        WriteVariant("alpha", "{ \"name\": \"alpha\" }");
        WriteVariant("broken", null);

        var catalog = Catalog();

        Assert.Equal(new[] { "alpha", "broken", "zeta" }, catalog.All.Select(v => v.Name));
        Assert.Equal(_root, catalog.Root);
        Assert.Null(catalog.Find("missing"));
        Assert.Null(catalog.Find(null));
    }

    [Fact]
    public void ACatalogOverAMissingRoot_IsEmpty()
    {
        var catalog = new BenchmarkInstructionVariantCatalog(
            Path.Combine(_root, "does-not-exist"), Handlers, NullLogger.Instance);

        Assert.Empty(catalog.All);
    }

    // -- Invalid variants ------------------------------------------------------------------------

    [Fact]
    public void AFolderWithoutAVariantFile_IsInvalid()
    {
        WriteVariant("empty-folder", null);

        var variant = Catalog().Find("empty-folder")!;

        Assert.False(variant.IsValid);
        Assert.Null(variant.Context);
        Assert.Contains(BenchmarkInstructionVariantCatalog.VariantFileName, variant.InvalidReason);
    }

    [Fact]
    public void AVariantFileThatDoesNotParse_IsInvalid()
    {
        WriteVariant("unparsed", "{ \"name\": ");

        var variant = Catalog().Find("unparsed")!;

        Assert.False(variant.IsValid);
        Assert.Contains("does not parse", variant.InvalidReason);
    }

    public static TheoryData<string, string, string> InvalidDefinitions => new()
    {
        { "name-mismatch", "{ \"name\": \"other-name\" }", "not the folder name" },
        { "Not_Kebab", "{ \"name\": \"Not_Kebab\" }", "kebab-case" },
        { "self-extending", "{ \"name\": \"self-extending\", \"extends\": \"self-extending\" }", "cannot extend itself" },
        { "no-parent", "{ \"name\": \"no-parent\", \"extends\": \"missing-parent\" }", "which is not a variant" },
        { "text-appendix", "{ \"name\": \"text-appendix\", \"promptAppendix\": \"appendix.txt\" }", "not a Markdown file of the folder" },
        { "missing-appendix", "{ \"name\": \"missing-appendix\", \"promptAppendix\": \"absent.md\" }", "not a Markdown file of the folder" },
        { "escaping-appendix", "{ \"name\": \"escaping-appendix\", \"promptAppendix\": \"../outside.md\" }", "not a Markdown file of the folder" },
        { "unknown-guide-tool", "{ \"name\": \"unknown-guide-tool\", \"toolGuides\": { \"no_such_tool\": \"guide.md\" } }", "not a registered tool" },
        { "missing-guide-file", "{ \"name\": \"missing-guide-file\", \"toolGuides\": { \"wiki_search\": \"absent.md\" } }", "not a file of the folder" },
        { "unknown-experimental", "{ \"name\": \"unknown-experimental\", \"experimentalTools\": [\"no_such_tool\"] }", "not a registered tool" },
        { "production-as-experimental", "{ \"name\": \"production-as-experimental\", \"experimentalTools\": [\"wiki_search\"] }", "not an experimental tool" },
        { "unknown-experiment", "{ \"name\": \"unknown-experiment\", \"experiments\": [\"time_travel\"] }", "not a known experiment" }
    };

    [Theory]
    [MemberData(nameof(InvalidDefinitions))]
    public void AnInvalidDefinition_IsReported_WithItsReason(string folder, string variantJson, string reason)
    {
        WriteVariant(folder, variantJson, ("appendix.txt", "Text."), ("guide.md", "Guide."));
        File.WriteAllText(Path.Combine(_root, "outside.md"), "Outside the folder.");

        var variant = Catalog().Find(folder)!;

        Assert.False(variant.IsValid);
        Assert.Null(variant.Context);
        Assert.Contains(reason, variant.InvalidReason);
    }

    [Fact]
    public void ANameLongerThan64Characters_IsInvalid()
    {
        string name = string.Join("-", Enumerable.Repeat("abcdefgh", 8));
        Assert.True(name.Length > 64);
        WriteVariant(name, "{ \"name\": \"" + name + "\" }");

        var variant = Catalog().Find(name)!;

        Assert.False(variant.IsValid);
        Assert.Contains("at most 64 characters", variant.InvalidReason);
    }

    [Fact]
    public void ExtendingAVariantThatItselfExtends_IsInvalid()
    {
        WriteVariant("grandparent", "{ \"name\": \"grandparent\" }");
        WriteVariant("parent", "{ \"name\": \"parent\", \"extends\": \"grandparent\" }");
        WriteVariant("child", "{ \"name\": \"child\", \"extends\": \"parent\" }");

        var catalog = Catalog();

        Assert.True(catalog.Find("grandparent")!.IsValid);
        Assert.True(catalog.Find("parent")!.IsValid);
        var child = catalog.Find("child")!;
        Assert.False(child.IsValid);
        Assert.Contains("one level is allowed", child.InvalidReason);
    }

    [Fact]
    public void ExtendingAnInvalidVariant_IsInvalid()
    {
        WriteVariant("bad-parent", "{ \"name\": \"bad-parent\", \"experiments\": [\"time_travel\"] }");
        WriteVariant("child", "{ \"name\": \"child\", \"extends\": \"bad-parent\" }");

        var child = Catalog().Find("child")!;

        Assert.False(child.IsValid);
        Assert.Contains("'bad-parent'", child.InvalidReason);
        Assert.Contains("invalid", child.InvalidReason);
    }

    // -- Extends ---------------------------------------------------------------------------------

    [Fact]
    public void AChild_OverridesItsParentsGuides_UnitesToolsAndExperiments_AndAppendsItsAppendix()
    {
        string parentFolder = WriteVariant("base-variant", """
            {
              "name": "base-variant",
              "promptAppendix": "appendix.md",
              "toolGuides": { "get_item_stats": "items.md", "get_monster_stats": "monsters.md" },
              "experimentalTools": ["get_character_info"],
              "experiments": ["stats_referenced_by"]
            }
            """,
            ("appendix.md", "Parent appendix."),
            ("items.md", "Parent item guide."),
            ("monsters.md", "Parent monster guide."));
        string childFolder = WriteVariant("child-variant", """
            {
              "name": "child-variant",
              "extends": "base-variant",
              "promptAppendix": "appendix.md",
              "toolGuides": { "get_item_stats": "items.md", "wiki_search": "wiki.md" },
              "experimentalTools": ["GET_CHARACTER_INFO"],
              "experiments": ["stats_referenced_by"]
            }
            """,
            ("appendix.md", "Child appendix.\n"),
            ("items.md", "Child item guide."),
            ("wiki.md", "Child wiki guide."));

        var child = Catalog().Find("child-variant")!;

        Assert.True(child.IsValid, child.InvalidReason);
        Assert.Equal("base-variant", child.Extends);
        var context = child.Context!;
        Assert.Equal("Parent appendix.\n\nChild appendix.", context.Appendix);
        Assert.Equal("Child item guide.", context.ToolGuides["get_item_stats"]);
        Assert.Equal("Parent monster guide.", context.ToolGuides["get_monster_stats"]);
        Assert.Equal("Child wiki guide.", context.ToolGuides["wiki_search"]);
        Assert.Equal(3, context.ToolGuides.Count);
        Assert.Equal(new[] { "get_character_info" }, context.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment }, context.Experiments);

        string parentHash = BenchmarkService.ComputeToolGuidesSha256(parentFolder)!;
        string ownHash = BenchmarkService.ComputeToolGuidesSha256(childFolder)!;
        Assert.Equal(Sha256($"parent:{parentHash}\nself:{ownHash}\n"), context.Hash);
        Assert.NotEqual(ownHash, context.Hash);
    }

    [Fact]
    public void AChildWithoutAnAppendix_InheritsItsParents()
    {
        WriteVariant("base-variant", "{ \"name\": \"base-variant\", \"promptAppendix\": \"appendix.md\" }",
            ("appendix.md", "  Parent appendix.  "));
        WriteVariant("child-variant", "{ \"name\": \"child-variant\", \"extends\": \"base-variant\" }");

        Assert.Equal("Parent appendix.", Catalog().Find("child-variant")!.Context!.Appendix);
    }

    [Fact]
    public void EditingTheParent_MovesTheChildsHash()
    {
        WriteVariant("base-variant", "{ \"name\": \"base-variant\", \"promptAppendix\": \"appendix.md\" }",
            ("appendix.md", "Parent appendix."));
        WriteVariant("child-variant", "{ \"name\": \"child-variant\", \"extends\": \"base-variant\" }");
        string before = Catalog().Find("child-variant")!.Context!.Hash;

        File.WriteAllText(Path.Combine(_root, "base-variant", "appendix.md"), "Parent appendix, edited.");
        string after = Catalog().Find("child-variant")!.Context!.Hash;

        Assert.NotEqual(before, after);
    }

    // -- Stamps ----------------------------------------------------------------------------------

    [Fact]
    public void StampOf_AndNameOfStamp_RoundTrip()
    {
        string hash = new string('c', 64);

        Assert.Equal("source-tools-v1:cccccccccccc", BenchmarkInstructionVariantCatalog.StampOf("source-tools-v1", hash));
        Assert.Equal("short:abc", BenchmarkInstructionVariantCatalog.StampOf("short", "abc"));
        Assert.Equal("source-tools-v1", BenchmarkInstructionVariantCatalog.NameOfStamp("source-tools-v1:cccccccccccc"));
        Assert.Equal("no-colon", BenchmarkInstructionVariantCatalog.NameOfStamp("no-colon"));
        Assert.Equal(new[] { "stats_referenced_by" }, BenchmarkInstructionVariantCatalog.KnownExperiments);
    }

    // -- The production tool-guide hash ----------------------------------------------------------

    [Fact]
    public void TheProductionToolGuidesHash_ReadsOnlyTheToolGuidesFolder()
    {
        string? production = BenchmarkService.ComputeToolGuidesSha256();

        Assert.Equal(
            BenchmarkService.ComputeToolGuidesSha256(Path.Combine(AppContext.BaseDirectory, "ToolGuides")),
            production);

        WriteVariant("probe-variant", "{ \"name\": \"probe-variant\", \"promptAppendix\": \"appendix.md\" }",
            ("appendix.md", "A variant that must not move the production hash."));
        Assert.True(Catalog().Find("probe-variant")!.IsValid);

        Assert.Equal(production, BenchmarkService.ComputeToolGuidesSha256());
    }

    [Fact]
    public void TheToolGuidesHash_OfAMissingOrEmptyFolder_IsNull()
    {
        Assert.Null(BenchmarkService.ComputeToolGuidesSha256(Path.Combine(_root, "missing")));
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        Assert.Null(BenchmarkService.ComputeToolGuidesSha256(Path.Combine(_root, "empty")));
    }

    // -- The shipped variants --------------------------------------------------------------------

    [Fact]
    public void TheShippedVariants_LoadValid()
    {
        string shipped = Path.Combine(AppContext.BaseDirectory, "BenchmarkVariants");
        Assert.SkipUnless(Directory.Exists(shipped), "BenchmarkVariants is not copied to the test output.");

        var catalog = new BenchmarkInstructionVariantCatalog(shipped, Handlers, NullLogger.Instance);

        var sourceTools = catalog.Find("source-tools-v1");
        Assert.NotNull(sourceTools);
        Assert.True(sourceTools!.IsValid, sourceTools.InvalidReason);
        Assert.Null(sourceTools.Extends);
        Assert.Contains("get_character_info", sourceTools.Context!.ExperimentalTools);
        Assert.True(sourceTools.Context.HasExperiment(BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment));
        Assert.True(sourceTools.Context.ToolGuides.ContainsKey("get_character_info"));

        var askerProfile = catalog.Find("asker-profile-v1");
        Assert.NotNull(askerProfile);
        Assert.True(askerProfile!.IsValid, askerProfile.InvalidReason);
        Assert.Equal("source-tools-v1", askerProfile.Extends);
        Assert.False(string.IsNullOrWhiteSpace(askerProfile.Context!.Appendix));
        Assert.Contains("get_character_info", askerProfile.Context.ExperimentalTools);
        Assert.True(askerProfile.Context.ToolGuides.ContainsKey("wiki_search"));
        Assert.True(askerProfile.Context.ToolGuides.ContainsKey("get_item_stats"));
        Assert.NotEqual(sourceTools.Context.Hash, askerProfile.Context.Hash);
    }

    [Fact]
    public void TheShippedVariants_AreInvalid_WithoutTheExperimentalTool()
    {
        string shipped = Path.Combine(AppContext.BaseDirectory, "BenchmarkVariants");
        Assert.SkipUnless(Directory.Exists(shipped), "BenchmarkVariants is not copied to the test output.");

        var handlers = Handlers.Where(h => h.ToolName != "get_character_info").ToList();
        var catalog = new BenchmarkInstructionVariantCatalog(shipped, handlers, NullLogger.Instance);

        Assert.False(catalog.Find("source-tools-v1")!.IsValid);
        Assert.False(catalog.Find("asker-profile-v1")!.IsValid);
    }

    // -- The launcher's view ---------------------------------------------------------------------

    [Fact]
    public void TheShippedVariants_DescribeWhatTheyContain()
    {
        string shipped = Path.Combine(AppContext.BaseDirectory, "BenchmarkVariants");
        Assert.SkipUnless(Directory.Exists(shipped), "BenchmarkVariants is not copied to the test output.");

        var catalog = new BenchmarkInstructionVariantCatalog(shipped, Handlers, NullLogger.Instance);

        var sourceTools = BenchmarkInstructionVariantDto.From(catalog.Find("source-tools-v1")!);
        Assert.Equal(new[] { "get_character_info" }, sourceTools.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment }, sourceTools.Experiments);
        Assert.Equal(new[] { "get_artifact_stats", "get_item_stats", "get_monster_stats" }, sourceTools.RewordedTools);
        Assert.False(sourceTools.HasPromptAppendix);
        Assert.Empty(sourceTools.RewordingsWithheldUnderSourceOnly);
        Assert.Equal(12, sourceTools.HashPrefix!.Length);

        var askerProfile = BenchmarkInstructionVariantDto.From(catalog.Find("asker-profile-v1")!);
        Assert.Equal(new[] { "get_character_info" }, askerProfile.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment }, askerProfile.Experiments);
        Assert.Equal(new[] { "get_artifact_stats", "get_item_stats", "get_monster_stats", "wiki_search" }, askerProfile.RewordedTools);
        Assert.True(askerProfile.HasPromptAppendix);
        Assert.Equal(new[] { "wiki_search" }, askerProfile.RewordingsWithheldUnderSourceOnly);
        Assert.Equal("source-tools-v1", askerProfile.Extends);
    }

    [Fact]
    public void AnInvalidVariant_DescribesNothing()
    {
        WriteVariant("empty-folder", null);

        var dto = BenchmarkInstructionVariantDto.From(Catalog().Find("empty-folder")!);

        Assert.False(dto.IsValid);
        Assert.Null(dto.HashPrefix);
        Assert.Empty(dto.ExperimentalTools);
        Assert.Empty(dto.Experiments);
        Assert.Empty(dto.RewordedTools);
        Assert.False(dto.HasPromptAppendix);
        Assert.Empty(dto.RewordingsWithheldUnderSourceOnly);
    }

    // -- Features --------------------------------------------------------------------------------

    private static BenchmarkInstructionVariantCatalog ShippedCatalog()
    {
        string shipped = Path.Combine(AppContext.BaseDirectory, "BenchmarkVariants");
        Assert.SkipUnless(Directory.Exists(shipped), "BenchmarkVariants is not copied to the test output.");
        return new BenchmarkInstructionVariantCatalog(shipped, Handlers, NullLogger.Instance);
    }

    [Fact]
    public void TheShippedVariants_DeriveTheirFeatures()
    {
        var catalog = ShippedCatalog();

        var sourceTools = catalog.Find("source-tools-v1")!;
        Assert.Equal(
            new[]
            {
                "tool.get_character_info", "experiment.stats_referenced_by",
                "guide.get_artifact_stats", "guide.get_item_stats", "guide.get_monster_stats"
            },
            sourceTools.Features.Select(f => f.Id));
        Assert.All(sourceTools.Features, f => Assert.False(f.FromParent));

        var askerProfile = catalog.Find("asker-profile-v1")!;
        Assert.Equal(
            new[]
            {
                "tool.get_character_info", "experiment.stats_referenced_by",
                "guide.get_artifact_stats", "guide.get_item_stats", "guide.get_monster_stats",
                "guide.wiki_search", "appendix.asker-profile-v1"
            },
            askerProfile.Features.Select(f => f.Id));
        Assert.Equal(
            new[] { true, true, true, true, true, false, false },
            askerProfile.Features.Select(f => f.FromParent));
        Assert.Equal(
            new[]
            {
                BenchmarkVariantFeatureKind.ExperimentalTool, BenchmarkVariantFeatureKind.Experiment,
                BenchmarkVariantFeatureKind.RewordedGuide, BenchmarkVariantFeatureKind.RewordedGuide,
                BenchmarkVariantFeatureKind.RewordedGuide, BenchmarkVariantFeatureKind.RewordedGuide,
                BenchmarkVariantFeatureKind.Appendix
            },
            askerProfile.Features.Select(f => f.Kind));
        Assert.Equal(
            new[] { "get_character_info", "stats_referenced_by", "get_artifact_stats", "get_item_stats", "get_monster_stats", "wiki_search", "asker-profile-v1" },
            askerProfile.Features.Select(f => f.Target));

        var tool = askerProfile.Features[0];
        Assert.Equal(askerProfile.Context!.ToolGuides["get_character_info"], tool.Text);
        Assert.Equal("get_character_info", tool.Handler!.ToolName);
        Assert.Null(askerProfile.Features[1].Text);
        Assert.Null(askerProfile.Features[1].Handler);
        Assert.Equal(askerProfile.Context.Appendix, askerProfile.Features[6].Text);
    }

    [Fact]
    public void TheShippedVariants_MentionsAreFoundMechanically()
    {
        var catalog = ShippedCatalog();
        var askerProfile = catalog.Find("asker-profile-v1")!;
        var mentions = askerProfile.Features.ToDictionary(f => f.Id, f => f.Mentions);

        // Each reworded stats guide ends with a paragraph about referenced_by; the appendix names get_character_info.
        Assert.Equal(new[] { "experiment.stats_referenced_by" }, mentions["guide.get_artifact_stats"]);
        Assert.Equal(new[] { "experiment.stats_referenced_by" }, mentions["guide.get_item_stats"]);
        Assert.Equal(new[] { "experiment.stats_referenced_by" }, mentions["guide.get_monster_stats"]);
        Assert.Equal(new[] { "tool.get_character_info" }, mentions["appendix.asker-profile-v1"]);
        Assert.Empty(mentions["tool.get_character_info"]);
        Assert.Empty(mentions["experiment.stats_referenced_by"]);

        var sourceTools = catalog.Find("source-tools-v1")!;
        Assert.Equal(new[] { "experiment.stats_referenced_by" },
            sourceTools.Features.Single(f => f.Id == "guide.get_item_stats").Mentions);
    }

    [Fact]
    public void TheShippedVariants_FeaturesApplyUnderTheConditionsTheirToolsReach()
    {
        var askerProfile = ShippedCatalog().Find("asker-profile-v1")!;
        var byId = askerProfile.Features.ToDictionary(f => f.Id);

        Assert.All(askerProfile.Features, f => Assert.True(f.AppliesUnder(BenchmarkInformationCondition.Production)));
        Assert.Equal(new[] { "appendix.asker-profile-v1" },
            askerProfile.Features.Where(f => f.AppliesUnder(BenchmarkInformationCondition.ClosedBook)).Select(f => f.Id));
        Assert.False(byId["guide.wiki_search"].AppliesUnder(BenchmarkInformationCondition.SourceOnly));
        Assert.True(byId["guide.get_item_stats"].AppliesUnder(BenchmarkInformationCondition.SourceOnly));
        Assert.True(byId["tool.get_character_info"].AppliesUnder(BenchmarkInformationCondition.SourceOnly));
        Assert.True(byId["experiment.stats_referenced_by"].AppliesUnder(BenchmarkInformationCondition.SourceOnly));

        var dto = BenchmarkInstructionVariantDto.From(askerProfile);
        Assert.Equal(askerProfile.Features.Select(f => f.Id), dto.Features.Select(f => f.Id));
        var wikiGuide = dto.Features.Single(f => f.Id == "guide.wiki_search");
        Assert.Equal("rewordedGuide", wikiGuide.Kind);
        Assert.Equal("wiki_search", wikiGuide.Target);
        Assert.False(wikiGuide.FromParent);
        Assert.True(wikiGuide.AppliesUnder.Production);
        Assert.False(wikiGuide.AppliesUnder.SourceOnly);
        Assert.False(wikiGuide.AppliesUnder.ClosedBook);
        Assert.Equal(new[] { "tool.get_character_info" }, dto.Features.Single(f => f.Kind == "appendix").Mentions);
        Assert.Equal("experimentalTool", dto.Features[0].Kind);
        Assert.Equal("experiment", dto.Features[1].Kind);
    }

    [Fact]
    public void AnInvalidVariant_HasNoFeatures()
    {
        WriteVariant("empty-folder", null);

        var variant = Catalog().Find("empty-folder")!;

        Assert.Empty(variant.Features);
        Assert.Empty(BenchmarkInstructionVariantDto.From(variant).Features);
    }

    [Fact]
    public void FindingAPlainName_ReturnsTodaysContext_ForBothShippedVariants()
    {
        string shipped = Path.Combine(AppContext.BaseDirectory, "BenchmarkVariants");
        var catalog = ShippedCatalog();

        string sourceFolder = Path.Combine(shipped, "source-tools-v1");
        string askerFolder = Path.Combine(shipped, "asker-profile-v1");
        string sourceHash = BenchmarkService.ComputeToolGuidesSha256(sourceFolder)!;
        string askerOwnHash = BenchmarkService.ComputeToolGuidesSha256(askerFolder)!;
        var sourceGuides = new Dictionary<string, string>
        {
            ["get_character_info"] = File.ReadAllText(Path.Combine(sourceFolder, "get_character_info.md")),
            ["get_artifact_stats"] = File.ReadAllText(Path.Combine(sourceFolder, "get_artifact_stats.md")),
            ["get_item_stats"] = File.ReadAllText(Path.Combine(sourceFolder, "get_item_stats.md")),
            ["get_monster_stats"] = File.ReadAllText(Path.Combine(sourceFolder, "get_monster_stats.md"))
        };
        var askerGuides = new Dictionary<string, string>(sourceGuides)
        {
            ["wiki_search"] = File.ReadAllText(Path.Combine(askerFolder, "wiki_search.md"))
        };

        var source = catalog.Find("source-tools-v1")!;
        Assert.Same(catalog.All.Single(v => v.Name == "source-tools-v1"), source);
        Assert.Null(source.Ablation);
        var sourceContext = source.Context!;
        Assert.Equal("source-tools-v1", sourceContext.Name);
        Assert.Equal(sourceHash, sourceContext.Hash);
        Assert.Equal("source-tools-v1:" + sourceHash[..12], sourceContext.Stamp);
        Assert.Null(sourceContext.Appendix);
        Assert.Empty(sourceContext.AppendixParts);
        Assert.Equal(sourceGuides.OrderBy(g => g.Key), sourceContext.ToolGuides.OrderBy(g => g.Key));
        Assert.Equal(new[] { "get_character_info" }, sourceContext.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment }, sourceContext.Experiments);

        var asker = catalog.Find("asker-profile-v1")!;
        Assert.Same(catalog.All.Single(v => v.Name == "asker-profile-v1"), asker);
        var askerContext = asker.Context!;
        string askerHash = Sha256($"parent:{sourceHash}\nself:{askerOwnHash}\n");
        string appendix = File.ReadAllText(Path.Combine(askerFolder, "appendix.md")).Trim();
        Assert.Equal("asker-profile-v1", askerContext.Name);
        Assert.Equal(askerHash, askerContext.Hash);
        Assert.Equal("asker-profile-v1:" + askerHash[..12], askerContext.Stamp);
        Assert.Equal(appendix, askerContext.Appendix);
        Assert.Equal(new[] { new BenchmarkVariantAppendixPart("asker-profile-v1", appendix) }, askerContext.AppendixParts);
        Assert.Equal(askerGuides.OrderBy(g => g.Key), askerContext.ToolGuides.OrderBy(g => g.Key));
        Assert.Equal(new[] { "get_character_info" }, askerContext.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment }, askerContext.Experiments);
    }

    // -- Ablations -------------------------------------------------------------------------------

    /// <summary>
    /// A parent with an appendix, a reworded item guide and an experiment, and a child that adds an
    /// experimental tool with its guide, a reworded wiki guide and its own appendix.
    /// </summary>
    private BenchmarkInstructionVariantCatalog AblationCatalog()
    {
        WriteVariant("base-probe", """
            {
              "name": "base-probe",
              "promptAppendix": "appendix.md",
              "toolGuides": { "get_item_stats": "items.md" },
              "experiments": ["stats_referenced_by"]
            }
            """,
            ("appendix.md", "Parent appendix."),
            ("items.md", "Item guide; referenced_by lists the functions."));
        WriteVariant("child-probe", """
            {
              "name": "child-probe",
              "extends": "base-probe",
              "promptAppendix": "appendix.md",
              "toolGuides": { "get_character_info": "character.md", "wiki_search": "wiki.md" },
              "experimentalTools": ["get_character_info"]
            }
            """,
            ("appendix.md", "Child appendix: read get_character_info first."),
            ("character.md", "Character guide."),
            ("wiki.md", "Wiki guide."));
        WriteVariant("single", "{ \"name\": \"single\", \"promptAppendix\": \"appendix.md\" }",
            ("appendix.md", "The only paragraph."));
        WriteVariant("broken", null);
        return Catalog();
    }

    [Fact]
    public void AChildsFeatures_SeparateItsAndItsParentsAppendix()
    {
        var child = AblationCatalog().Find("child-probe")!;

        Assert.Equal(
            new[]
            {
                "tool.get_character_info", "experiment.stats_referenced_by", "guide.get_item_stats",
                "guide.wiki_search", "appendix.base-probe", "appendix.child-probe"
            },
            child.Features.Select(f => f.Id));
        Assert.Equal(new[] { false, true, true, false, true, false }, child.Features.Select(f => f.FromParent));
        Assert.Equal(
            new[] { new BenchmarkVariantAppendixPart("base-probe", "Parent appendix."), new BenchmarkVariantAppendixPart("child-probe", "Child appendix: read get_character_info first.") },
            child.Context!.AppendixParts);
        Assert.Equal("Parent appendix.\n\nChild appendix: read get_character_info first.", child.Context.Appendix);
        Assert.Equal(new[] { "experiment.stats_referenced_by" }, child.Features.Single(f => f.Id == "guide.get_item_stats").Mentions);
        Assert.Equal(new[] { "tool.get_character_info" }, child.Features.Single(f => f.Id == "appendix.child-probe").Mentions);
        Assert.Empty(child.Features.Single(f => f.Id == "appendix.base-probe").Mentions);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "missing/minus/tool.get_character_info", "Its base 'missing' is not a variant." },
        { "broken/minus/tool.get_character_info", "Its base 'broken' is invalid: " },
        { "child-probe/without/tool.get_character_info", "'without' is not an ablation mode; use 'minus' or 'only'." },
        { "child-probe/minus/tool.wiki_search", "'tool.wiki_search' is not a feature of 'child-probe'; its features are " },
        { "single/minus/appendix.single", "leaves nothing; choose None." },
        { "single/only/appendix.single", "is the whole variant; choose it." },
        { "child-probe/minus", "An ablation is written <variant>/minus/<feature> or <variant>/only/<feature>." },
        { "child-probe//tool.get_character_info", "An ablation is written" }
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AnAblationSpec_IsRefused_AsAnInvalidEntryWithItsReason(string spec, string reason)
    {
        var catalog = AblationCatalog();

        var entry = catalog.Find(spec);

        Assert.NotNull(entry);
        Assert.False(entry!.IsValid);
        Assert.Null(entry.Context);
        Assert.Empty(entry.Features);
        Assert.Equal(spec, entry.Name);
        Assert.Contains(reason, entry.InvalidReason);
    }

    [Fact]
    public void AnUnknownNameWithoutASlash_IsStillNotFound()
    {
        var catalog = AblationCatalog();

        Assert.Null(catalog.Find("child-probe-minus"));
        Assert.Null(catalog.Find(null));
    }

    [Fact]
    public void Minus_RemovesOneFeature_AndKeepsTheRest()
    {
        var catalog = AblationCatalog();
        var whole = catalog.Find("child-probe")!.Context!;

        var noTool = catalog.Find("child-probe/minus/tool.get_character_info")!;
        Assert.True(noTool.IsValid, noTool.InvalidReason);
        Assert.Empty(noTool.Context!.ExperimentalTools);
        Assert.False(noTool.Context.ToolGuides.ContainsKey("get_character_info"));
        Assert.Equal(new[] { "get_item_stats", "wiki_search" }, noTool.Context.ToolGuides.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(whole.Experiments, noTool.Context.Experiments);
        Assert.Equal(whole.Appendix, noTool.Context.Appendix);
        Assert.Equal(whole.AppendixParts, noTool.Context.AppendixParts);

        var noGuide = catalog.Find("child-probe/minus/guide.wiki_search")!.Context!;
        Assert.False(noGuide.ToolGuides.ContainsKey("wiki_search"));
        Assert.Equal("Character guide.", noGuide.ToolGuides["get_character_info"]);
        Assert.Equal(whole.ExperimentalTools, noGuide.ExperimentalTools);

        var noExperiment = catalog.Find("child-probe/minus/experiment.stats_referenced_by")!.Context!;
        Assert.False(noExperiment.HasExperiment(BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment));
        Assert.Equal(whole.ToolGuides.Count, noExperiment.ToolGuides.Count);

        var noParentAppendix = catalog.Find("child-probe/minus/appendix.base-probe")!.Context!;
        Assert.Equal("Child appendix: read get_character_info first.", noParentAppendix.Appendix);
        Assert.Equal("child-probe", Assert.Single(noParentAppendix.AppendixParts).Variant);

        var noOwnAppendix = catalog.Find("child-probe/minus/appendix.child-probe")!.Context!;
        Assert.Equal("Parent appendix.", noOwnAppendix.Appendix);
        Assert.Equal(whole.ToolGuides.Count, noOwnAppendix.ToolGuides.Count);
    }

    [Fact]
    public void Only_KeepsOneFeature_AndNothingElse()
    {
        var catalog = AblationCatalog();

        var tool = catalog.Find("child-probe/only/tool.get_character_info")!.Context!;
        Assert.Equal(new[] { "get_character_info" }, tool.ExperimentalTools);
        Assert.Equal("Character guide.", Assert.Single(tool.ToolGuides).Value);
        Assert.Empty(tool.Experiments);
        Assert.Null(tool.Appendix);
        Assert.Empty(tool.AppendixParts);

        var experiment = catalog.Find("child-probe/only/experiment.stats_referenced_by")!.Context!;
        Assert.Equal(new[] { BenchmarkInstructionVariantCatalog.StatsReferencedByExperiment }, experiment.Experiments);
        Assert.Empty(experiment.ExperimentalTools);
        Assert.Empty(experiment.ToolGuides);
        Assert.Null(experiment.Appendix);

        var guide = catalog.Find("child-probe/only/guide.get_item_stats")!.Context!;
        Assert.Equal("get_item_stats", Assert.Single(guide.ToolGuides).Key);
        Assert.Empty(guide.ExperimentalTools);
        Assert.Empty(guide.Experiments);

        var appendix = catalog.Find("child-probe/only/appendix.child-probe")!.Context!;
        Assert.Equal("Child appendix: read get_character_info first.", appendix.Appendix);
        Assert.Empty(appendix.ToolGuides);
        Assert.Empty(appendix.ExperimentalTools);
    }

    [Fact]
    public void AnAblation_IsNamedBySpec_AndHashedOverItsBase()
    {
        var catalog = AblationCatalog();
        var whole = catalog.Find("child-probe")!;
        const string Spec = "child-probe/minus/guide.wiki_search";

        var ablation = catalog.Find(Spec)!;

        Assert.Equal(Spec, ablation.Name);
        Assert.Equal(Spec, ablation.Context!.Name);
        Assert.Equal(whole.Description, ablation.Description);
        Assert.Equal("base-probe", ablation.Extends);
        Assert.Equal(Sha256($"base:{whole.Context!.Hash}\nminus:guide.wiki_search\n"), ablation.Context.Hash);
        Assert.Equal(Spec + ":" + ablation.Context.Hash[..12], ablation.Context.Stamp);
        Assert.Equal(Spec, BenchmarkInstructionVariantCatalog.NameOfStamp(ablation.Context.Stamp));
        Assert.NotEqual(whole.Context.Hash, ablation.Context.Hash);
        Assert.NotEqual(ablation.Context.Hash, catalog.Find("child-probe/only/guide.wiki_search")!.Context!.Hash);

        Assert.Equal("child-probe", ablation.Ablation!.BaseName);
        Assert.Equal(BenchmarkInstructionVariantCatalog.MinusMode, ablation.Ablation.Mode);
        Assert.True(ablation.Ablation.IsMinus);
        Assert.Equal("guide.wiki_search", ablation.Ablation.Feature.Id);
        Assert.DoesNotContain(ablation.Features, f => f.Id == "guide.wiki_search");
        Assert.Equal(5, ablation.Features.Count);
    }

    [Fact]
    public void AnAblation_IsCached_AndNeverListed()
    {
        var catalog = AblationCatalog();
        const string Spec = "child-probe/only/tool.get_character_info";

        var first = catalog.Find(Spec);
        var second = catalog.Find(Spec);

        Assert.Same(first, second);
        Assert.Equal(new[] { "base-probe", "broken", "child-probe", "single" }, catalog.All.Select(v => v.Name));
    }

    [Fact]
    public void EditingTheBase_MovesTheAblationsHash()
    {
        const string Spec = "child-probe/minus/tool.get_character_info";
        string before = AblationCatalog().Find(Spec)!.Context!.Stamp;

        File.WriteAllText(Path.Combine(_root, "base-probe", "items.md"), "Item guide, edited.");
        string after = Catalog().Find(Spec)!.Context!.Stamp;

        Assert.NotEqual(before, after);
        Assert.Equal(BenchmarkInstructionVariantCatalog.NameOfStamp(before), BenchmarkInstructionVariantCatalog.NameOfStamp(after));
    }

    [Fact]
    public void TheLongestStamp_FitsTheColumn_AndALongerSpecIsRefused()
    {
        string name = new string('a', 64);
        string fitting = new string('x', 70);
        string tooLong = new string('y', 71);
        var handlers = Handlers.Concat(new IToolHandler[] { new FakeToolHandler(fitting), new FakeToolHandler(tooLong) }).ToList();
        WriteVariant(name, "{ \"name\": \"" + name + "\", \"promptAppendix\": \"appendix.md\", \"toolGuides\": { \""
            + fitting + "\": \"x.md\", \"" + tooLong + "\": \"y.md\" } }",
            ("appendix.md", "Paragraph."), ("x.md", "X guide."), ("y.md", "Y guide."));
        var catalog = Catalog(handlers);
        Assert.True(catalog.Find(name)!.IsValid, catalog.Find(name)!.InvalidReason);

        var fits = catalog.Find($"{name}/minus/guide.{fitting}")!;
        Assert.True(fits.IsValid, fits.InvalidReason);
        Assert.Equal(BenchmarkInstructionVariantCatalog.MaxStampLength, fits.Context!.Stamp.Length);
        Assert.Equal(160, BenchmarkInstructionVariantCatalog.MaxStampLength);

        var refused = catalog.Find($"{name}/minus/guide.{tooLong}")!;
        Assert.False(refused.IsValid);
        Assert.Contains("161 characters, more than the 160", refused.InvalidReason);

        Assert.True(catalog.Find($"{name}/only/guide.{tooLong}")!.IsValid);
    }

    [Fact]
    public void DescribeAblation_NamesTheFeatureInWords_AndNothingForAPlainName()
    {
        Assert.Equal("asker-profile-v1, leaving out the reworded wiki_search description",
            BenchmarkInstructionVariantCatalog.DescribeAblation("asker-profile-v1/minus/guide.wiki_search"));
        Assert.Equal("asker-profile-v1, only the get_character_info tool",
            BenchmarkInstructionVariantCatalog.DescribeAblation("asker-profile-v1/only/tool.get_character_info"));
        Assert.Equal("source-tools-v1, leaving out the stats_referenced_by tool-output switch",
            BenchmarkInstructionVariantCatalog.DescribeAblation("source-tools-v1/minus/experiment.stats_referenced_by"));
        Assert.Equal("asker-profile-v1, only the asker-profile-v1 instruction paragraph",
            BenchmarkInstructionVariantCatalog.DescribeAblation("asker-profile-v1/only/appendix.asker-profile-v1"));
        Assert.Null(BenchmarkInstructionVariantCatalog.DescribeAblation("asker-profile-v1"));
        Assert.Null(BenchmarkInstructionVariantCatalog.DescribeAblation("asker-profile-v1/sideways/tool.get_character_info"));
    }
}
