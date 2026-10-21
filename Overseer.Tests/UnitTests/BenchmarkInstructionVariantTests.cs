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
}
