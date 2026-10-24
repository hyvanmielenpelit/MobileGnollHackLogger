namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// <see cref="SourceReferenceIndex"/>: the functions referencing an identifier, over a synthetic
/// corpus and over the real GnollHack source when the clone is present; the makedefs constant
/// derivation; and the stats tools' <c>referenced_by</c> field, present only under the
/// <c>stats_referenced_by</c> experiment.
/// </summary>
public class SourceReferenceIndexTests : IDisposable
{
    private readonly string _rootDir;

    public SourceReferenceIndexTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "SourceReferenceIndexTests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private static IReadOnlyList<string> Lines(string text) => text.Split('\n');

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SyntheticCorpus =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/zap.c"] =
                Lines(
                    "/* ART_FIXTURE in a comment, before any function */\n" +
                    "boolean\n" +
                    "zap_fixture(struct obj *otmp)\n" +
                    "{\n" +
                    "    /* ART_FIXTURE in a block comment */\n" +
                    "    pline(\"ART_FIXTURE in a string\");\n" +
                    "    // ART_FIXTURE in a line comment\n" +
                    "    return otmp->oartifact == ART_FIXTURE;\n" +
                    "}\n" +
                    "\n" +
                    "#if 0\n" +
                    "void\n" +
                    "stale_fixture(void)\n" +
                    "{\n" +
                    "    use(ART_FIXTURE);\n" +
                    "}\n" +
                    "#endif\n" +
                    "\n" +
                    "void\n" +
                    "only_lookalikes(void)\n" +
                    "{\n" +
                    "    use(ART_FIXTURE_TWO, XART_FIXTURE, \"ART_FIXTURE\", 'A');\n" +
                    "}\n"),
            ["src/artifact.c"] =
                Lines(
                    "static const int fixture_table[] = { ART_FIXTURE, 0 };\n" +
                    "\n" +
                    "boolean\n" +
                    "is_fixture(struct obj *obj)\n" +
                    "{\n" +
                    "    if (!obj)\n" +
                    "        return FALSE;\n" +
                    "    return obj->oartifact == ART_FIXTURE;\n" +
                    "}\n" +
                    "\n" +
                    "boolean\n" +
                    "has_fixture(struct monst *mon)\n" +
                    "{\n" +
                    "    short key = artilist[ART_FIXTURE].otyp;\n" +
                    "    if (key == ART_FIXTURE)\n" +
                    "        return TRUE;\n" +
                    "    return FALSE;\n" +
                    "}\n"),
            ["include/artifact.h"] =
                Lines(
                    "#define is_fixture_macro(o) ((o)->oartifact == ART_FIXTURE)\n" +
                    "int header_fixture(int a) { return a == ART_FIXTURE; }\n"),
            ["src/sub/nested.c"] =
                Lines(
                    "void\n" +
                    "nested_fixture(void)\n" +
                    "{\n" +
                    "    use(ART_FIXTURE);\n" +
                    "}\n")
        };

    private static SourceReferenceIndex SyntheticIndex() => new(() => SyntheticCorpus);

    [Fact]
    public void Synthetic_FunctionsInFileThenLineOrder_DistinctByFunction_FirstReferenceLine()
    {
        var references = SyntheticIndex().FindReferencingFunctions("ART_FIXTURE");

        Assert.Equal(
            new[] { "is_fixture (src/artifact.c:8)", "has_fixture (src/artifact.c:14)", "zap_fixture (src/zap.c:8)" },
            references);
    }

    [Fact]
    public void Synthetic_CommentsStringsCharactersAndIfZero_AreNotReferences()
    {
        var references = SyntheticIndex().FindReferencingFunctions("ART_FIXTURE")!;

        Assert.DoesNotContain(references, r => r.StartsWith("stale_fixture", StringComparison.Ordinal));
        Assert.DoesNotContain(references, r => r.StartsWith("only_lookalikes", StringComparison.Ordinal));
        Assert.Single(references, r => r.StartsWith("zap_fixture", StringComparison.Ordinal));
        Assert.Equal(new[] { "only_lookalikes (src/zap.c:22)" }, SyntheticIndex().FindReferencingFunctions("ART_FIXTURE_TWO"));
    }

    [Fact]
    public void Synthetic_PastTheMaximum_EndsWithTheCountOfTheRest()
    {
        var references = SyntheticIndex().FindReferencingFunctions("ART_FIXTURE", max: 1);

        Assert.Equal(new[] { "is_fixture (src/artifact.c:8)", "… and 2 more" }, references);
    }

    [Fact]
    public void Synthetic_NoReference_IsAnEmptyList_AndANonIdentifierIsNull()
    {
        var index = SyntheticIndex();

        Assert.Empty(index.FindReferencingFunctions("ART_NOWHERE")!);
        Assert.Null(index.FindReferencingFunctions("ART FIXTURE"));
        Assert.Null(index.FindReferencingFunctions(""));
    }

    [Fact]
    public void Synthetic_AnswerIsMemoizedPerBuild_AndRebuiltForANewCorpus()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> corpus = SyntheticCorpus;
        var index = new SourceReferenceIndex(() => corpus);

        var first = index.FindReferencingFunctions("ART_FIXTURE");
        Assert.Same(first, index.FindReferencingFunctions("ART_FIXTURE"));

        corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/new.c"] = Lines("void\nnewer(void)\n{\n    use(ART_FIXTURE);\n}\n")
        };
        Assert.Equal(new[] { "newer (src/new.c:4)" }, index.FindReferencingFunctions("ART_FIXTURE"));
    }

    [Fact]
    public void UnavailableCorpus_IsNull()
    {
        var index = new SourceReferenceIndex(() => throw new InvalidOperationException("not ready"));

        Assert.Null(index.FindReferencingFunctions("ART_FIXTURE"));
    }

    [Theory]
    [InlineData("The Master Key of Thievery", "ART_MASTER_KEY_OF_THIEVERY")]
    [InlineData("Excalibur", "ART_EXCALIBUR")]
    [InlineData("The Platinum Yendorian Express Card", "ART_YENDORIAN_EXPRESS_CARD")]
    [InlineData("Platinum Yendorian Express Card", "ART_YENDORIAN_EXPRESS_CARD")]
    [InlineData("The Eye of the Aethiopica", "ART_EYE_OF_THE_AETHIOPICA")]
    [InlineData("Orcrist-2", "ART_ORCRIST__")]
    [InlineData("Triple-Headed Flail of Yeenaghu", "ART_TRIPLE_HEADED_FLAIL_OF_YEENAGHU")]
    [InlineData("A Name That Runs Well Past Thirty-Six Characters In All", "ART_A_NAME_THAT_RUNS_WELL_PAST_THIRTY_SI")]
    public void ArtifactConstant_FollowsMakedefs(string name, string expected)
    {
        Assert.Equal(expected, SourceReferenceIndex.ArtifactConstant(name));
    }

    [Theory]
    [InlineData("werejackal", "S_HUMAN", "PM_HUMAN_WEREJACKAL")]
    [InlineData("werejackal", "S_DOG", "PM_WEREJACKAL")]
    [InlineData("gnoll lord", "S_GNOLL", "PM_GNOLL_LORD")]
    [InlineData("Woodland-elf", "S_HUMAN", "PM_WOODLAND_ELF")]
    public void MonsterConstant_FollowsMakedefs(string name, string monsterClass, string expected)
    {
        Assert.Equal(expected, SourceReferenceIndex.MonsterConstant(name, monsterClass));
    }

    [Theory]
    [InlineData("digging", "WAND_CLASS", "MAT_WOOD", "WAN_DIGGING")]
    [InlineData("digging", "SPBOOK_CLASS", "MAT_PAPER", "SPE_DIGGING")]
    [InlineData("gain level", "POTION_CLASS", "MAT_GLASS", "POT_GAIN_LEVEL")]
    [InlineData("conflict", "RING_CLASS", "MAT_IRON", "RIN_CONFLICT")]
    [InlineData("genocide", "SCROLL_CLASS", "MAT_PAPER", "SCR_GENOCIDE")]
    [InlineData("long sword", "WEAPON_CLASS", "MAT_IRON", "LONG_SWORD")]
    [InlineData("cheap plastic imitation of the Amulet of Yendor", "AMULET_CLASS", "MAT_PLASTIC", "FAKE_AMULET_OF_YENDOR")]
    [InlineData("Amulet of Yendor", "AMULET_CLASS", "MAT_MITHRIL", "AMULET_OF_YENDOR")]
    [InlineData("worthless piece of yellowish brown glass", "GEM_CLASS", "MAT_GLASS", "WORTHLESS_PIECE_OF_YELLOWISH_BROWN_GLASS")]
    [InlineData("protection from shape changers", "RING_CLASS", "MAT_IRON", "RIN_PROTECTION_FROM_SHAPE_CHANGERS")]
    [InlineData("a scroll name of well over thirty-six characters", "SCROLL_CLASS", "MAT_PAPER", "SCR_A_SCROLL_NAME_OF_WELL_OVER_THIRTY_SI")]
    [InlineData("a weapon name of well over forty characters in length", "WEAPON_CLASS", "MAT_IRON", "A_WEAPON_NAME_OF_WELL_OVER_FORTY_CHARACT")]
    public void ObjectConstant_FollowsMakedefs(string name, string objectClass, string material, string expected)
    {
        Assert.Equal(expected, SourceReferenceIndex.ObjectConstant(name, objectClass, material));
    }

    /* ==================================================================================
     * The real GnollHack source.
     * =============================================================================== */

    private const string GnollHackSourceDir = @"C:\hmp\GnollHack";

    private static readonly Lazy<SourceReferenceIndex?> RealIndex = new(() =>
    {
        if (!File.Exists(Path.Combine(GnollHackSourceDir, "src", "artifact.c"))) return null;
        var corpus = SourceLivenessIndex.LoadCorpus(GnollHackSourceDir, 2000 * 1024L);
        return new SourceReferenceIndex(() => corpus);
    });

    [Fact]
    public void RealSource_MasterKeyOfThievery_IsReferencedByTheMagicKeyChecks()
    {
        var index = RealIndex.Value;
        if (index == null) Assert.Skip("The GnollHack working copy was not found under " + GnollHackSourceDir + ".");

        var references = index!.FindReferencingFunctions("ART_MASTER_KEY_OF_THIEVERY");

        Assert.NotNull(references);
        Assert.Contains(references!, r => r.StartsWith("is_magic_key (src/artifact.c:", StringComparison.Ordinal));
        Assert.Contains(references!, r => r.StartsWith("has_magic_key (src/artifact.c:", StringComparison.Ordinal));
        Assert.DoesNotContain(references!, r => r.Contains("src/role.c", StringComparison.Ordinal));
    }

    /* ==================================================================================
     * The stats tools' referenced_by field.
     * =============================================================================== */

    /// <summary>An include/artilist.h entry with the 34 arguments the A() macro takes.</summary>
    private const string FixtureArtilistH =
        "    A(\"The Fixture Blade\", None, None, LONG_SWORD, LONG_SWORD,\n" +
        "        MAT_NONE, EXCEPTIONALITY_ELITE, MYTHIC_PREFIX_NONE, MYTHIC_SUFFIX_NONE,\n" +
        "        AF_NONE, AF2_NONE, SPFX_NONE, SPFX_NONE, 0,\n" +
        "        1, 5, 0, PHYS(5, 10), NO_POWER, NO_POWER, NO_POWER, 0, 0, 0, 0, 0, A_NONE, NON_PM, NON_PM, 1000L,\n" +
        "        NO_COLOR, NO_COLOR, 0, OBJECT_SOUNDSET_NONE),\n";

    private const string FixtureOnamesH =
        "#ifndef ONAMES_H\n" +
        "#define ONAMES_H\n" +
        "#define\tART_FIXTURE_BLADE\t1\n" +
        "#endif /* ONAMES_H */\n";

    private const string FixtureArtifactC =
        "boolean\n" +
        "wields_fixture(struct obj *otmp)\n" +
        "{\n" +
        "    return (boolean) (otmp && otmp->oartifact == ART_FIXTURE_BLADE);\n" +
        "}\n";

    private async Task<(SourceCodeService Service, IConfiguration Config)> CreateArtifactServiceAsync(string onamesH = FixtureOnamesH)
    {
        string sourceDir = Path.Combine(_rootDir, "stats_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "include"));
        File.WriteAllText(Path.Combine(sourceDir, "include", "artilist.h"), FixtureArtilistH);
        File.WriteAllText(Path.Combine(sourceDir, "include", "onames.h"), onamesH);
        File.WriteAllText(Path.Combine(sourceDir, "src", "artifact.c"), FixtureArtifactC);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.InitializationTask;
        return (service, config);
    }

    private static Task<ToolResult> ExecuteAsync(IToolHandler tool, ToolExecutionContext context)
    {
        var element = JsonDocument.Parse("{\"name\": \"The Fixture Blade\"}").RootElement;
        return tool.ExecuteAsync(element, context, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ArtifactStats_WithoutTheExperiment_HasNoReferencedBy()
    {
        var (service, config) = await CreateArtifactServiceAsync();
        using (service)
        {
            var tool = new GetArtifactStatsTool(service, config);
            var plain = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4201) };
            var otherExperiment = new ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4202),
                Variant = new BenchmarkVariantContext { Name = "other", Experiments = new[] { "some_other_switch" } }
            };

            /* The response serialized as the tool always has, with no referenced_by member. */
            string expected = JsonSerializer.Serialize(service.GetArtifactStats("The Fixture Blade"), new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            foreach (var context in new[] { plain, otherExperiment })
            {
                var result = await ExecuteAsync(tool, context.CloneFor("call-1"));
                Assert.True(result.Success, result.ErrorMessage);
                Assert.Contains("The Fixture Blade", result.Content);
                Assert.DoesNotContain("referenced_by", result.Content);
                Assert.Equal(expected, result.Content);
            }
        }
    }

    [Fact]
    public async Task ArtifactStats_WithTheExperiment_AfterCloneFor_ListsTheReferencingFunctions()
    {
        var (service, config) = await CreateArtifactServiceAsync();
        using (service)
        {
            var tool = new GetArtifactStatsTool(service, config);
            var context = new ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4203),
                Variant = new BenchmarkVariantContext
                {
                    Name = "fixture",
                    Experiments = new[] { BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment }
                }
            };

            var result = await ExecuteAsync(tool, context.CloneFor("call-2"));

            Assert.True(result.Success, result.ErrorMessage);
            using var doc = JsonDocument.Parse(result.Content);
            Assert.Equal(
                new[] { "wields_fixture (src/artifact.c:4)" },
                doc.RootElement.GetProperty("referenced_by").EnumerateArray().Select(e => e.GetString()));
        }
    }

    [Fact]
    public async Task ArtifactStats_ConstantNotInOnames_OmitsReferencedBy()
    {
        var (service, config) = await CreateArtifactServiceAsync("#define\tART_SOMETHING_ELSE\t1\n");
        using (service)
        {
            var tool = new GetArtifactStatsTool(service, config);
            var context = new ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4204),
                Variant = new BenchmarkVariantContext { Experiments = new[] { BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment } }
            };

            var result = await ExecuteAsync(tool, context.CloneFor("call-3"));

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains("The Fixture Blade", result.Content);
            Assert.DoesNotContain("referenced_by", result.Content);
        }
    }
}
