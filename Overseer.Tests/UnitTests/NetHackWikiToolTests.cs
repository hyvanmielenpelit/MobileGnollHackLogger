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
    public async Task NetHackWikiService_ContentFingerprint_MovesWithAnEditedFile()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", _tempDir),
                new System.Collections.Generic.KeyValuePair<string, string?>("MaxNetHackWikiFileSizeKB", "200")
            })
            .Build();
        int fileCount = Directory.GetFiles(_tempDir, "*.md", SearchOption.AllDirectories).Length;

        CorpusContentFingerprint? sequential;
        using (var service = new NetHackWikiService(config, null, 1))
        {
            await service.InitializationTask;
            sequential = service.ContentFingerprint;
        }

        CorpusContentFingerprint? parallel;
        using (var service = new NetHackWikiService(config, null, 4))
        {
            await service.InitializationTask;
            parallel = service.ContentFingerprint;
        }

        Assert.NotNull(sequential);
        Assert.Equal(fileCount, sequential!.FileCount);
        Assert.Equal(sequential.Sha256, parallel!.Sha256);

        File.AppendAllText(Path.Combine(_tempDir, "Elbereth.md"), "\nScuffed engravings stop working.\n");

        using var rebuilt = new NetHackWikiService(config, null, 4);
        await rebuilt.InitializationTask;

        Assert.NotNull(rebuilt.ContentFingerprint);
        Assert.Equal(fileCount, rebuilt.ContentFingerprint!.FileCount);
        Assert.NotEqual(sequential.Sha256, rebuilt.ContentFingerprint.Sha256);
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
    public async Task NetHackWikiSearchTool_ParameterSchema_HasNoNamespaceFilter()
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

        var properties = searchTool.ParameterSchema.GetProperty("properties");
        Assert.False(properties.TryGetProperty("namespace_filter", out _));
        Assert.True(properties.TryGetProperty("query", out _));
        Assert.True(properties.TryGetProperty("max_results", out _));
    }

    [Fact]
    public async Task NetHackWikiSearchTool_NamespaceFilterArgument_IsIgnored()
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
        var searchTool = new NetHackWikiSearchTool(service, config);
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), SpoilerFreeMode = false };

        var unfiltered = await searchTool.ExecuteAsync(
            JsonDocument.Parse("{\"query\": \"cockatrice\"}").RootElement, context, CancellationToken.None);
        var filtered = await searchTool.ExecuteAsync(
            JsonDocument.Parse("{\"query\": \"cockatrice\", \"namespace_filter\": \"source\"}").RootElement, context, CancellationToken.None);

        Assert.True(unfiltered.Success);
        Assert.True(filtered.Success);
        Assert.Contains("Cockatrice", filtered.Content);
        Assert.Equal(unfiltered.Content, filtered.Content);
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
        Assert.Equal(
            "No NetHack wiki article matched 'NonExistentArticleXYZ'. No candidates found. Try nethack_wiki_search.",
            result.Content);
    }

    /// <summary>
    /// A miss the resolver's own candidate list already covers (the summary query hit, even though
    /// the title/filename query missed entirely): the miss payload names the article, lists that
    /// candidate, and points at nethack_wiki_search as the next action.
    /// </summary>
    [Fact]
    public async Task NetHackWikiViewTool_Miss_WithResolverCandidates_ListsThemAndNamesNextAction()
    {
        var missDir = Path.Combine(Path.GetTempPath(), "NetHackWikiViewMissTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missDir);
        try
        {
            var fooContent = @"---
title: ""Foo""
namespace: article
summary: ""An article about zzgnollbarqqterm, a made-up term used only here.""
---

Foo has no mention of the search term in its title or body.
";
            File.WriteAllText(Path.Combine(missDir, "Foo.md"), fooContent);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", missDir)
                })
                .Build();

            using var service = new NetHackWikiService(config);
            await service.InitializationTask;
            var viewTool = new NetHackWikiViewTool(service);

            var jsonParams = JsonDocument.Parse("{\"article\": \"zzgnollbarqqterm\"}").RootElement;
            var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(3001), SpoilerFreeMode = false };

            var result = await viewTool.ExecuteAsync(jsonParams, context, CancellationToken.None);

            Assert.True(result.Success);
            Assert.StartsWith("No NetHack wiki article matched 'zzgnollbarqqterm'.", result.Content);
            Assert.Contains("Candidates: Foo.", result.Content);
            Assert.Contains("Try nethack_wiki_search.", result.Content);
            Assert.True(result.Content!.Length <= 600);
        }
        finally
        {
            if (Directory.Exists(missDir))
            {
                Directory.Delete(missDir, true);
            }
        }
    }

    /// <summary>
    /// Indexing in parallel chunks keeps document ids in file order: with equal scores, search hits
    /// come back in the order Directory.GetFiles lists the files.
    /// </summary>
    [Fact]
    public async Task NetHackWikiService_ChunkedIndexing_KeepsFileOrder()
    {
        var orderDir = Path.Combine(Path.GetTempPath(), "NetHackWikiOrderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orderDir);
        try
        {
            for (int i = 1; i <= 24; i++)
            {
                File.WriteAllText(Path.Combine(orderDir, $"Page{i:D2}.md"), $"---\ntitle: Page {i:D2}\n---\n\nzorkmid zorkmid\n");
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", orderDir)
                })
                .Build();

            using var service = new NetHackWikiService(config, null, indexingParallelism: 4);
            await service.InitializationTask;

            var expected = Directory.GetFiles(orderDir, "*.md", SearchOption.AllDirectories)
                .Select(f => $"--- Page {Path.GetFileNameWithoutExtension(f).Substring(4)} ---")
                .ToList();
            var actual = service.GetRelevantContext("zorkmid", null, 24)
                .Select(r => r.Split('\n')[0])
                .ToList();

            Assert.Equal(24, actual.Count);
            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(orderDir))
            {
                Directory.Delete(orderDir, true);
            }
        }
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

    // ---------------------------------------------------------------------------------------
    // Section-less over-cap notice.
    // ---------------------------------------------------------------------------------------

    private const string OverCapTitle = "Longread Treatise";
    private const string OverCapHeader = "--- " + OverCapTitle + " ---\n";
    private const int OverCapRenderedLength = 26000;

    // Filler with no heading marker and no newline, so it can be cut to any exact length without
    // disturbing heading parsing.
    private const string OverCapFillerUnit =
        "Dungeon filler prose pads this article well past the result cap without a single heading marker. ";

    private const string ShortArticleBody = "Brief Note is short.\n\n## Only Section\nNothing more to say.\n";

    private static string RepeatTo(string unit, int length)
    {
        var sb = new System.Text.StringBuilder(length);
        while (sb.Length < length)
        {
            sb.Append(unit);
        }
        sb.Length = length;
        return sb.ToString();
    }

    /// <summary>
    /// The over-cap article's body: four headings, the last of them far past the 10,000-character
    /// cap, padded so the rendered article (its <c>--- Title ---</c> header included) totals exactly
    /// <see cref="OverCapRenderedLength"/> characters.
    /// </summary>
    private static string BuildOverCapBody()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Longread Treatise opens with an introduction.\n\n## Overview\n");
        sb.Append(RepeatTo(OverCapFillerUnit, 6000));
        sb.Append("\n\n## Generation\n");
        sb.Append(RepeatTo(OverCapFillerUnit, 6000));
        sb.Append("\n\n## Strategy\n");
        sb.Append(RepeatTo(OverCapFillerUnit, 6000));
        sb.Append("\n\n## Late Notes\n");
        sb.Append(RepeatTo(OverCapFillerUnit, OverCapRenderedLength - OverCapHeader.Length - sb.Length));
        return sb.ToString();
    }

    /// <summary>
    /// Runs <c>nethack_wiki_view</c> once against a corpus of its own holding the over-cap
    /// "Longread Treatise" and the short "Brief Note".
    /// </summary>
    private static async Task<ToolResult> ViewInOverCapCorpusAsync(string jsonParams, ToolExecutionContext context)
    {
        var corpusDir = Path.Combine(Path.GetTempPath(), "NetHackWikiOverCapTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(corpusDir);
        try
        {
            File.WriteAllText(
                Path.Combine(corpusDir, "Longread_Treatise.md"),
                $"---\ntitle: \"{OverCapTitle}\"\nnamespace: article\nsummary: \"An article longer than the result cap.\"\n---\n\n{BuildOverCapBody()}");
            File.WriteAllText(
                Path.Combine(corpusDir, "Brief_Note.md"),
                $"---\ntitle: \"Brief Note\"\nnamespace: article\nsummary: \"A short article.\"\n---\n\n{ShortArticleBody}");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string?>("NetHackWikiPath", corpusDir)
                })
                .Build();

            using var service = new NetHackWikiService(config);
            await service.InitializationTask;
            var viewTool = new NetHackWikiViewTool(service);

            return await viewTool.ExecuteAsync(JsonDocument.Parse(jsonParams).RootElement, context, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(corpusDir))
            {
                Directory.Delete(corpusDir, true);
            }
        }
    }

    [Fact]
    public async Task NetHackWikiViewTool_SectionlessArticleOverCap_PrependsNoticeAndLandsAtExactlyTheCap()
    {
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4001), SpoilerFreeMode = false };

        var result = await ViewInOverCapCorpusAsync("{\"article\": \"Longread Treatise\"}", context);

        Assert.True(result.Success);
        string content = result.Content!;

        Assert.Equal(context.MaxResultLength, content.Length);
        Assert.StartsWith($"[Article is {OverCapRenderedLength} characters; the first ", content);
        Assert.Contains(
            " are shown. Headings: Overview; Generation; Strategy; Late Notes. Call nethack_wiki_view again with section set to one of them to read the rest.]\n" + OverCapHeader + "Longread Treatise opens",
            content);
        Assert.DoesNotContain("[No NetHack wiki article titled", content);
        Assert.DoesNotContain("[Truncated:", content);

        // The notice names exactly the number of article characters that follow it.
        string noticeLine = content.Substring(0, content.IndexOf('\n'));
        int shown = int.Parse(Regex.Match(noticeLine, @"the first (\d+) are shown").Groups[1].Value);
        Assert.Equal(content.Length - noticeLine.Length - 1, shown);
    }

    [Fact]
    public async Task NetHackWikiViewTool_SectionlessArticleOverCap_InSpoilerFreeMode_KeepsTheSuffixWithinTheCap()
    {
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4002), SpoilerFreeMode = true };

        var result = await ViewInOverCapCorpusAsync("{\"article\": \"Longread Treatise\"}", context);

        Assert.True(result.Success);
        Assert.StartsWith($"[Article is {OverCapRenderedLength} characters; the first ", result.Content);
        Assert.EndsWith("Only share mechanics, not unrevealed content.]", result.Content);
        Assert.Equal(context.MaxResultLength, result.Content!.Length);
    }

    [Fact]
    public async Task NetHackWikiViewTool_SectionlessArticleUnderCap_IsReturnedUnchanged()
    {
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4003), SpoilerFreeMode = false };

        var result = await ViewInOverCapCorpusAsync("{\"article\": \"Brief Note\"}", context);

        Assert.True(result.Success);
        Assert.Equal("--- Brief Note ---\n" + ShortArticleBody, result.Content);
    }

    [Fact]
    public async Task NetHackWikiViewTool_NonExactRequestForAnArticleOverCap_KeepsTheResolutionLineFirst()
    {
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(4004), SpoilerFreeMode = false };

        var result = await ViewInOverCapCorpusAsync("{\"article\": \"Longread\"}", context);

        Assert.True(result.Success);
        string content = result.Content!;

        Assert.StartsWith(
            $"[No NetHack wiki article titled 'Longread'. Showing '{OverCapTitle}'.]\n[Article is {OverCapRenderedLength} characters; the first ",
            content);
        Assert.Contains(
            "Headings: Overview; Generation; Strategy; Late Notes. Call nethack_wiki_view again with section set to one of them to read the rest.]\n" + OverCapHeader,
            content);
        Assert.DoesNotContain("[Truncated:", content);
        Assert.Equal(context.MaxResultLength, content.Length);
    }
}

