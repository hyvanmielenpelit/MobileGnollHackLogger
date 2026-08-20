using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class NetHackWikiToolTests : IDisposable
{
    private readonly string _tempDir;

    public NetHackWikiToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NetHackWikiToolTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Create test markdown articles with YAML frontmatter
        var cockatriceContent = @"---
title: ""Cockatrice""
namespace: article
summary: ""A cockatrice, 'c', is a type of monster that appears in NetHack.""
---

--- Monster Stats ---
Difficulty: 8
Level: 5

A cockatrice, 'c', is a small monster.

## Generation
Randomly-generated cockatrices are always hostile.

## Strategy
Always wear gloves when handling cockatrice corpses.
";

        var elberethContent = @"---
title: ""Elbereth""
namespace: article
summary: ""Elbereth is a magical warding engraving.""
---

Elbereth is an engraving that scares monsters.

## Effects
Non-humanoid monsters will flee.
";

        var sourceContent = @"---
title: ""Source:NetHack 3.4.3/src/objects.c""
namespace: source
summary: ""Annotated source code for objects.c in NetHack 3.4.3.""
---

/* NetHack 3.4.3 objects.c */
#include ""hack.h""
";

        File.WriteAllText(Path.Combine(_tempDir, "Cockatrice.md"), cockatriceContent);
        File.WriteAllText(Path.Combine(_tempDir, "Elbereth.md"), elberethContent);
        File.WriteAllText(Path.Combine(_tempDir, "Source__NetHack_3.4.3__src__objects.c.md"), sourceContent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch { }
        }
    }

    [Fact]
    public async Task NetHackWikiService_IndexesAndRetrievesArticles()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir),
                new System.Collections.Generic.KeyValuePair<string, string?>("MaxNetHackWikiFileSizeKB", "200")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;

        var article = service.GetArticle("Cockatrice");
        Assert.NotNull(article);
        Assert.Contains("--- Cockatrice ---", article);
        Assert.Contains("Difficulty: 8", article);

        var section = service.GetArticle("Cockatrice", "Strategy");
        Assert.NotNull(section);
        Assert.Contains("Strategy", section);
        Assert.Contains("Always wear gloves", section);
        Assert.DoesNotContain("Difficulty: 8", section);
    }

    [Fact]
    public async Task NetHackWikiService_NamespaceFilter_WorksCorrectly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir),
                new System.Collections.Generic.KeyValuePair<string, string?>("MaxNetHackWikiFileSizeKB", "200")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;

        var sourceResults = service.GetRelevantContext("objects.c", "source", 5).ToList();
        Assert.Single(sourceResults);
        Assert.Contains("Source:NetHack 3.4.3/src/objects.c", sourceResults[0]);

        var articleResults = service.GetRelevantContext("objects.c", "article", 5).ToList();
        Assert.Empty(articleResults);
    }

    [Fact]
    public async Task NetHackWikiSearchTool_ExecutesQuery_ReturnsExpectedResults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir),
                new System.Collections.Generic.KeyValuePair<string, string?>("Tools:nethack_wiki_search:MaxResults", "5")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;
        var searchTool = new NetHackWikiSearchTool(service, config);

        var jsonParams = JsonDocument.Parse("{\"query\": \"cockatrice\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = false };

        var result = await searchTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Cockatrice", result.Content);
        Assert.DoesNotContain("SPOILER-FREE MODE ACTIVE", result.Content);
    }

    [Fact]
    public async Task NetHackWikiSearchTool_SpoilerFreeMode_AppendsReminder()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir)
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;
        var searchTool = new NetHackWikiSearchTool(service, config);

        var jsonParams = JsonDocument.Parse("{\"query\": \"cockatrice\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = true };

        var result = await searchTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Cockatrice", result.Content);
        Assert.Contains("SPOILER-FREE MODE ACTIVE", result.Content);
    }

    [Fact]
    public async Task NetHackWikiViewTool_ExecutesArticleView_ReturnsContent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir)
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;
        var viewTool = new NetHackWikiViewTool(service);

        var jsonParams = JsonDocument.Parse("{\"article\": \"Elbereth\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = false };

        var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Elbereth is an engraving", result.Content);
    }

    [Fact]
    public async Task NetHackWikiService_RealData_IndexesAndQueriesSuccessfully()
    {
        if (!Directory.Exists(@"c:\hmp\nethackwiki")) return;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", @"c:\hmp\nethackwiki"),
                new System.Collections.Generic.KeyValuePair<string, string?>("MaxNetHackWikiFileSizeKB", "500")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;

        var cockatrice = service.GetArticle("Cockatrice");
        Assert.NotNull(cockatrice);
        Assert.Contains("cockatrice", cockatrice, StringComparison.OrdinalIgnoreCase);

        var evilHack = service.GetArticle("EvilHack");
        Assert.NotNull(evilHack);

        var strategy = service.GetArticle("Cockatrice", "Strategy");
        Assert.NotNull(strategy);
        Assert.Contains("Strategy", strategy);

        var searchResults = service.GetRelevantContext("wand of digging", "article", 3).ToList();
        Assert.NotEmpty(searchResults);
        Assert.Contains(searchResults, r => r.Contains("digging", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NetHackWikiService_UnconfiguredPath_DoesNotThrowAndReturnsGracefully()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", "")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;

        var article = service.GetArticle("Cockatrice");
        Assert.Null(article);

        var results = service.GetRelevantContext("cockatrice").ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfigHealthService_WhenNetHackWikiPathMissing_ReturnsAlert(string? path)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("SentryDSN", "https://key@sentry.io/123"),
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", path)
            })
            .Build();

        var healthService = new ConfigHealthService(config);
        var alerts = healthService.GetSystemAlerts().ToList();

        var wikiAlert = alerts.FirstOrDefault(a => a.Id == "nethack-wiki-path-missing");
        Assert.NotNull(wikiAlert);
        Assert.Equal("warning", wikiAlert.Type);
        Assert.Contains("NetHackWikiPath", wikiAlert.Message);
    }

    [Fact]
    public void ConfigHealthService_WhenNetHackWikiPathConfigured_NoAlert()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("SentryDSN", "https://key@sentry.io/123"),
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", @"c:\hmp\nethackwiki")
            })
            .Build();

        var healthService = new ConfigHealthService(config);
        var alerts = healthService.GetSystemAlerts().ToList();

        Assert.DoesNotContain(alerts, a => a.Id == "nethack-wiki-path-missing");
    }

    [Fact]
    public async Task NetHackWikiSearchTool_WhenIndexingComplete_NotFoundQuery_ReturnsStandardNotFoundMessage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir),
                new System.Collections.Generic.KeyValuePair<string, string?>("Tools:nethack_wiki_search:MaxResults", "5")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask; // Ensure indexing has finished

        var searchTool = new NetHackWikiSearchTool(service, config);
        var jsonParams = JsonDocument.Parse("{\"query\": \"nonexistent_term_12345\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = false };

        var result = await searchTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("No relevant information found in the NetHack wiki.", result.Content);
    }

    [Fact]
    public async Task NetHackWikiViewTool_WhenIndexingComplete_NotFoundArticle_ReturnsStandardNotFoundMessage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir)
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask; // Ensure indexing has finished

        var viewTool = new NetHackWikiViewTool(service);
        var jsonParams = JsonDocument.Parse("{\"article\": \"NonExistentArticleXYZ\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = false };

        var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("NetHack wiki article matching 'NonExistentArticleXYZ' not found.", result.Content);
    }

    [Fact]
    public async Task NetHackWikiSearchTool_WhenIndexingInProgress_ReturnsDirectiveError()
    {
        if (!Directory.Exists(@"c:\hmp\nethackwiki")) return;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", @"c:\hmp\nethackwiki")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        // Do NOT await service.InitializationTask to test warm-up / in-progress state
        if (!service.IsIndexingComplete)
        {
            var searchTool = new NetHackWikiSearchTool(service, config);
            var jsonParams = JsonDocument.Parse("{\"query\": \"cockatrice\"}").RootElement;
            var context = new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = false };

            var result = await searchTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
            Assert.False(result.Success);
            Assert.Equal(ToolGuardMessages.NetHackWikiIndexingInProgress, result.ErrorMessage);
            Assert.Contains("Do not retry", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task NetHackWikiViewTool_WhenIndexingInProgress_ReturnsDirectiveError()
    {
        if (!Directory.Exists(@"c:\hmp\nethackwiki")) return;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", @"c:\hmp\nethackwiki")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        // Do NOT await service.InitializationTask to test warm-up / in-progress state
        if (!service.IsIndexingComplete)
        {
            var viewTool = new NetHackWikiViewTool(service);
            var jsonParams = JsonDocument.Parse("{\"article\": \"Cockatrice\"}").RootElement;
            var context = new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = false };

            var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
            Assert.False(result.Success);
            Assert.Equal(ToolGuardMessages.NetHackWikiIndexingInProgress, result.ErrorMessage);
            Assert.Contains("Do not retry", result.ErrorMessage);
        }
    }
}

