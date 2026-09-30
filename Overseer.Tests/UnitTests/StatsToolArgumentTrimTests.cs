namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// get_item_stats, get_monster_stats and get_artifact_stats trim the name argument, so a name with
/// leading or trailing whitespace returns the same result as the bare name, and a name that is
/// blank after trimming is a missing one. The stats cases run against the real src/objects.c,
/// include/objclass.h, src/monst.c and include/artilist.h, copied from the GnollHack clone when it
/// is present on this machine. get_knowledge_article trims its topic the same way, against a
/// synthetic knowledge base.
/// </summary>
public class StatsToolArgumentTrimTests : IDisposable
{
    private const string GnollHackSourceDir = @"c:\hmp\GnollHack";

    private static readonly string[] CorpusFiles =
    {
        Path.Combine("src", "objects.c"),
        Path.Combine("include", "objclass.h"),
        Path.Combine("src", "monst.c"),
        Path.Combine("include", "artilist.h")
    };

    private readonly string _rootDir;

    public StatsToolArgumentTrimTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "StatsToolArgumentTrimTests_" + Guid.NewGuid().ToString("N"));
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

    private static bool SourceAvailable =>
        Array.TrueForAll(CorpusFiles, f => File.Exists(Path.Combine(GnollHackSourceDir, f)));

    private static IConfiguration CreateConfiguration(string sourceDir)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "2000")
            })
            .Build();
    }

    private async Task<(SourceCodeService Service, IConfiguration Config)> CreateRealCorpusServiceAsync()
    {
        string sourceDir = Path.Combine(_rootDir, "real");
        Directory.CreateDirectory(Path.Combine(sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "include"));
        foreach (var file in CorpusFiles)
        {
            File.Copy(Path.Combine(GnollHackSourceDir, file), Path.Combine(sourceDir, file));
        }

        var config = CreateConfiguration(sourceDir);
        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        return (service, config);
    }

    private static IToolHandler CreateTool(string toolName, SourceCodeService service, IConfiguration config)
    {
        return toolName switch
        {
            "get_item_stats" => new GetItemStatsTool(service, config),
            "get_monster_stats" => new GetMonsterStatsTool(service, config),
            "get_artifact_stats" => new GetArtifactStatsTool(service, config),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, null)
        };
    }

    private static Task<ToolResult> ExecuteAsync(IToolHandler tool, object arguments)
    {
        var element = JsonDocument.Parse(JsonSerializer.Serialize(arguments)).RootElement;
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4001) };
        return tool.ExecuteAsync(element, context, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("get_item_stats", "long sword")]
    [InlineData("get_monster_stats", "goblin")]
    [InlineData("get_artifact_stats", "Excalibur")]
    public async Task PaddedName_ReturnsTheSameEntryAsTheBareName(string toolName, string name)
    {
        if (!SourceAvailable) return;

        var (service, config) = await CreateRealCorpusServiceAsync();
        using (service)
        {
            var tool = CreateTool(toolName, service, config);

            var bare = await ExecuteAsync(tool, new { name });
            Assert.True(bare.Success, bare.ErrorMessage);
            Assert.Contains(name, bare.Content);

            foreach (var padded in new[] { " " + name, name + " ", "  " + name + "\t", "\n" + name + " \r\n" })
            {
                var result = await ExecuteAsync(tool, new { name = padded });
                Assert.True(result.Success, result.ErrorMessage);
                Assert.Equal(bare.Content, result.Content);
            }
        }
    }

    [Fact]
    public async Task PaddedObjectClass_SelectsTheSameClassAsTheBareOne()
    {
        if (!SourceAvailable) return;

        var (service, config) = await CreateRealCorpusServiceAsync();
        using (service)
        {
            var tool = CreateTool("get_item_stats", service, config);

            var bare = await ExecuteAsync(tool, new { name = "digging", object_class = "WAND_CLASS" });
            Assert.True(bare.Success, bare.ErrorMessage);
            Assert.Contains("digging", bare.Content);

            var padded = await ExecuteAsync(tool, new { name = " digging ", object_class = " WAND_CLASS " });
            Assert.True(padded.Success, padded.ErrorMessage);
            Assert.Equal(bare.Content, padded.Content);
        }
    }

    [Theory]
    [InlineData("get_item_stats")]
    [InlineData("get_monster_stats")]
    [InlineData("get_artifact_stats")]
    public async Task WhitespaceOnlyName_ReturnsMissingNameError(string toolName)
    {
        string sourceDir = Path.Combine(_rootDir, "blank");
        Directory.CreateDirectory(Path.Combine(sourceDir, "src"));
        File.WriteAllText(Path.Combine(sourceDir, "src", "util.c"),
            "/* util.c */\r\n" +
            "int\r\n" +
            "bar(void)\r\n" +
            "{\r\n" +
            "    return 1;\r\n" +
            "}\r\n");

        var config = CreateConfiguration(sourceDir);
        using var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(service.IsIndexingComplete);

        var tool = CreateTool(toolName, service, config);

        var result = await ExecuteAsync(tool, new { name = " \t\r\n " });

        Assert.False(result.Success);
        Assert.Equal("Missing name parameter", result.ErrorMessage);
    }

    [Fact]
    public async Task PaddedTopic_ReturnsTheSameArticleAsTheBareTopic()
    {
        string kbDir = Path.Combine(_rootDir, "kb");
        Directory.CreateDirectory(Path.Combine(kbDir, "Content"));
        File.WriteAllText(Path.Combine(kbDir, "Content", "app_navigation.md"),
            "---\n" +
            "title: App Navigation\n" +
            "summary: How to move around the app.\n" +
            "---\n" +
            "Fixture article body.\n");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("KbPath", kbDir)
            })
            .Build();

        using var knowledgeBase = new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config);
        await knowledgeBase.InitializationTask;

        var tool = new KnowledgeBaseTool(knowledgeBase);

        var bare = await ExecuteAsync(tool, new { topic = "app_navigation" });
        Assert.True(bare.Success, bare.ErrorMessage);
        Assert.Contains("Fixture article body.", bare.Content);

        var padded = await ExecuteAsync(tool, new { topic = "  app_navigation \n" });
        Assert.True(padded.Success, padded.ErrorMessage);
        Assert.Equal(bare.Content, padded.Content);
    }
}
