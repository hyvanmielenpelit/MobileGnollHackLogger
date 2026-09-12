using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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

        // 5 articles sharing "wandlore", each with a 4,000-character body - over the tool's
        // 3,000-char PerResultChars cap, so a full MaxResults=5 yield carries five truncation notes.
        var wandloreBody = string.Concat(Enumerable.Repeat("wandlore lore ", 300)).Substring(0, 4000);
        for (int i = 1; i <= 5; i++)
        {
            var wandloreContent = $"---\ntitle: \"Wandlore{i}\"\nnamespace: article\nsummary: \"wandlore article {i}\"\n---\n\n{wandloreBody}";
            File.WriteAllText(Path.Combine(_tempDir, $"Wandlore{i}.md"), wandloreContent);
        }

        // Two articles for the title-resolution test: "Two weapon combat" has no article of its
        // own title/filename match, but Combat's title does, and Twoweapon's summary does.
        var twoWeaponContent = @"---
title: ""Twoweapon""
namespace: article
summary: ""Two-weapon combat is a fighting style requiring two weapons.""
---

Twoweapon is a fighting style that uses a weapon in each hand.
";

        var combatContent = @"---
title: ""Combat""
namespace: article
summary: ""General combat mechanics.""
---

Combat covers how attacks, to-hit rolls, and damage work.
";

        File.WriteAllText(Path.Combine(_tempDir, "Twoweapon.md"), twoWeaponContent);
        File.WriteAllText(Path.Combine(_tempDir, "Combat.md"), combatContent);

        // "Spellcasting" and "Spellcaster" stem to the same term under the English analyzer, so a
        // title/filename query for either name scores both documents equally - the exact-title
        // request must not be pushed out of the window by its close, equally-scored neighbor.
        var spellcastingContent = @"---
title: ""Spellcasting""
namespace: article
summary: ""Spellcasting is the practice of casting spells from spellbooks.""
---

Spellcasting is governed by intelligence and wisdom, and by experience level.
";

        var spellcasterContent = @"---
title: ""Spellcaster""
namespace: article
summary: ""A spellcaster is a monster capable of casting spells.""
---

Spellcasters include gnomish wizards and other spell-slinging monsters.
";

        File.WriteAllText(Path.Combine(_tempDir, "Spellcasting.md"), spellcastingContent);
        File.WriteAllText(Path.Combine(_tempDir, "Spellcaster.md"), spellcasterContent);
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
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = false };

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
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = true };

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
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = false };

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
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = false };

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
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = false };

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
            var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = false };

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
            var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = false };

            var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);
            Assert.False(result.Success);
            Assert.Equal(ToolGuardMessages.NetHackWikiIndexingInProgress, result.ErrorMessage);
            Assert.Contains("Do not retry", result.ErrorMessage);
        }
    }

    private class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => true;
        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = "Client result" });
        }
    }

    [Fact]
    public async Task NetHackWikiSearchTool_MaxResultLengthOverride_CoversFullFiveArticleYield()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir),
                new System.Collections.Generic.KeyValuePair<string, string?>("Tools:nethack_wiki_search:MaxResults", "5"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Tools:nethack_wiki_search:PerResultChars", "3000"),
                new System.Collections.Generic.KeyValuePair<string, string?>("ToolExecutionLimits:MaxProcessParallelToolCalls", "30"),
                new System.Collections.Generic.KeyValuePair<string, string?>("ToolExecutionLimits:MaxProcessExternalLookupCalls", "3"),
                new System.Collections.Generic.KeyValuePair<string, string?>("ToolExecutionLimits:MaxBatchResultLength", "40000")
            })
            .Build();

        using var service = new NetHackWikiService(config);
        await service.InitializationTask;
        var searchTool = new NetHackWikiSearchTool(service, config);

        Assert.True(searchTool.MaxResultLengthOverride.HasValue);
        Assert.True(searchTool.MaxResultLengthOverride!.Value >= 5 * 3000);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var executor = new ToolExecutor(
            new IToolHandler[] { searchTool },
            new NullClientBridge(),
            NullLogger<ToolExecutor>.Instance,
            cache,
            config);

        var jsonParams = JsonDocument.Parse("{\"query\": \"wandlore\", \"max_results\": 5}").RootElement;
        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(2001),
            SpoilerFreeMode = false,
            MaxResultLength = 5000,
            MaxCallsPerSession = 50
        };

        var result = await executor.ExecuteAsync("nethack_wiki_search", jsonParams, context, CancellationToken.None);

        Assert.True(result.Success);
        var headerMatches = Regex.Matches(result.Content, @"(?m)^--- .+ ---$");
        Assert.Equal(5, headerMatches.Count);
        Assert.Equal(5, Regex.Matches(result.Content, Regex.Escape("[Article truncated: showing 3000 of")).Count);
        Assert.True(result.Content.Length <= searchTool.MaxResultLengthOverride.Value,
            $"Result length {result.Content.Length} exceeded the {searchTool.MaxResultLengthOverride.Value}-char floor.");
        Assert.DoesNotContain("[Truncated:", result.Content);
    }

    [Fact]
    public async Task NetHackWikiViewTool_RequestResolvesToDifferentTitle_PrependsResolutionLine()
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

        var jsonParams = JsonDocument.Parse("{\"article\": \"Two weapon combat\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(2002), SpoilerFreeMode = false };

        var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.StartsWith("[No NetHack wiki article titled 'Two weapon combat'.", result.Content);
        Assert.Contains("Twoweapon", result.Content);
    }

    [Fact]
    public async Task NetHackWikiViewTool_RequestResolvesToExactTitle_OmitsResolutionLine()
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

        var jsonParams = JsonDocument.Parse("{\"article\": \"Combat\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(2003), SpoilerFreeMode = false };

        var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("[No NetHack wiki article titled", result.Content);
        Assert.StartsWith("--- Combat ---", result.Content);
    }

    [Fact]
    public async Task NetHackWikiViewTool_ExactTitleAmongEquallyScoredNeighbor_ReturnsExactArticle_OmitsResolutionLine()
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

        var jsonParams = JsonDocument.Parse("{\"article\": \"Spellcasting\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(2004), SpoilerFreeMode = false };

        var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("[No NetHack wiki article titled", result.Content);
        Assert.StartsWith("--- Spellcasting ---", result.Content);
    }

    [Fact]
    public async Task NetHackWikiViewTool_NonExactRequestAmongEquallyScoredNeighbors_StillPrependsResolutionLine()
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

        var jsonParams = JsonDocument.Parse("{\"article\": \"Spellcasters\"}").RootElement;
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(2005), SpoilerFreeMode = false };

        var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.StartsWith("[No NetHack wiki article titled 'Spellcasters'.", result.Content);
    }
}

