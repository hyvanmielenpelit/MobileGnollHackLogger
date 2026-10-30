using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MobileGnollHackLogger.Data;
using Overseer.Services.CorpusIndexing;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="CorpusToolUsageService"/> over EF InMemory: status counts and guard refusals per tool and
/// per corpus, nearest-rank percentiles, the UTC day buckets at the edges of the window, that only the
/// corpus tools are counted, which wiki a <c>wiki_overview</c> call counts under, the clamping of
/// <c>days</c>, and the per-period cache entry with <c>Size = 1</c>. Calls are dated by their
/// <see cref="ChatMessage"/>, so every call is seeded with one.
/// </summary>
public sealed class CorpusToolUsageServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 18, 15, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] AllCorpusTools =
    {
        "wiki_search", "wiki_view", "monster_lookup", "item_lookup", "wiki_overview",
        "nethack_wiki_search", "nethack_wiki_view", "wiki_overview",
        "get_knowledge_article"
    };

    private readonly ApplicationDbContext _db;
    private readonly MemoryCache _cache;
    private readonly CorpusToolUsageService _service;
    private ChatSession? _session;

    public CorpusToolUsageServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        /* With a SizeLimit, an entry set without a Size throws, so every test also proves Size is set. */
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10, TrackStatistics = true });
        _service = new CorpusToolUsageService(_db, _cache, new FixedClock(Now));
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    // -- helpers -------------------------------------------------------------------------------------

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTime utcNow)
        {
            _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Adds one assistant message at <paramref name="timestampUtc"/> carrying one tool call.</summary>
    private void AddCall(string? name, string? status, DateTime timestampUtc, int? executionMs = null, string? error = null, string argsText = "{}")
    {
        _session ??= _db.ChatSession.Add(new ChatSession
        {
            Title = "Corpus tool usage fixture",
            CreatedUtc = Today.AddDays(-100),
            LastMessageUtc = Now
        }).Entity;

        _db.ChatMessage.Add(new ChatMessage
        {
            ChatSession = _session,
            Role = "assistant",
            Content = "fixture reply",
            TimestampUtc = timestampUtc,
            ToolCalls =
            {
                new ChatMessageToolCall
                {
                    ToolCallId = "call_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Name = name,
                    Status = status,
                    Error = error,
                    ExecutionMs = executionMs,
                    ArgsText = argsText,
                    Result = status == "completed" ? "fixture result" : null
                }
            }
        });
    }

    private Task SaveAsync() => _db.SaveChangesAsync(TestContext.Current.CancellationToken);

    private static CorpusToolUsageToolDto ToolOf(CorpusToolUsageDto usage, string tool) => Assert.Single(usage.Tools, t => t.Tool == tool);

    private static CorpusToolUsageCorpusDto CorpusOf(CorpusToolUsageDto usage, CorpusKind corpus) => Assert.Single(usage.Corpora, c => c.Corpus == corpus);

    // -- counts ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_CountsStatusesAndGuardRefusalsPerToolAndCorpus()
    {
        var ct = TestContext.Current.CancellationToken;
        DateTime at = Now.AddHours(-1);
        AddCall("wiki_search", "completed", at, 10);
        AddCall("wiki_search", "completed", at, 20);
        AddCall("wiki_search", "error", at, 1, ToolGuardMessages.WikiIndexingInProgress);
        AddCall("wiki_search", "error", at, 30, "The search timed out.");
        AddCall("wiki_search", "error", at, 2, ToolGuardMessages.NetHackWikiIndexingInProgress);
        AddCall("wiki_search", "canceled", at);
        AddCall("wiki_search", null, at);
        AddCall("wiki_view", "error", at, 3, ToolGuardMessages.WikiIndexingInProgress);
        AddCall("nethack_wiki_view", "error", at, 4, ToolGuardMessages.NetHackWikiIndexingInProgress);
        AddCall("nethack_wiki_search", "error", at, 5, ToolGuardMessages.KnowledgeBaseIndexingInProgress);
        AddCall("get_knowledge_article", "completed", at, 6);
        AddCall("get_knowledge_article", "error", at, 7, ToolGuardMessages.KnowledgeBaseIndexingInProgress);
        await SaveAsync();

        var usage = await _service.GetAsync(30, ct);

        var wikiSearch = ToolOf(usage, "wiki_search");
        Assert.Equal(CorpusKind.GnollHackWiki, wikiSearch.Corpus);
        Assert.Equal(7, wikiSearch.Calls);
        Assert.Equal(2, wikiSearch.Completed);
        Assert.Equal(3, wikiSearch.Errors);
        Assert.Equal(2, wikiSearch.Other);
        Assert.Equal(1, wikiSearch.GuardRefusals);

        var wikiView = ToolOf(usage, "wiki_view");
        Assert.Equal(1, wikiView.Calls);
        Assert.Equal(1, wikiView.Errors);
        Assert.Equal(1, wikiView.GuardRefusals);

        var netHackView = ToolOf(usage, "nethack_wiki_view");
        Assert.Equal(1, netHackView.Errors);
        Assert.Equal(1, netHackView.GuardRefusals);

        var netHackSearch = ToolOf(usage, "nethack_wiki_search");
        Assert.Equal(1, netHackSearch.Errors);
        Assert.Equal(0, netHackSearch.GuardRefusals);

        var article = ToolOf(usage, "get_knowledge_article");
        Assert.Equal(CorpusKind.KnowledgeBase, article.Corpus);
        Assert.Equal(2, article.Calls);
        Assert.Equal(1, article.Completed);
        Assert.Equal(1, article.Errors);
        Assert.Equal(1, article.GuardRefusals);

        var gnollHack = CorpusOf(usage, CorpusKind.GnollHackWiki);
        Assert.Equal(8, gnollHack.Calls);
        Assert.Equal(2, gnollHack.Completed);
        Assert.Equal(4, gnollHack.Errors);
        Assert.Equal(2, gnollHack.Other);
        Assert.Equal(2, gnollHack.GuardRefusals);

        var netHack = CorpusOf(usage, CorpusKind.NetHackWiki);
        Assert.Equal(2, netHack.Calls);
        Assert.Equal(2, netHack.Errors);
        Assert.Equal(1, netHack.GuardRefusals);

        var knowledgeBase = CorpusOf(usage, CorpusKind.KnowledgeBase);
        Assert.Equal(2, knowledgeBase.Calls);
        Assert.Equal(1, knowledgeBase.Completed);
        Assert.Equal(1, knowledgeBase.GuardRefusals);
    }

    [Fact]
    public async Task GetAsync_NothingStored_ReturnsEveryToolAndThreeCorporaWithZeroCounts()
    {
        var ct = TestContext.Current.CancellationToken;

        var usage = await _service.GetAsync(30, ct);

        Assert.Equal(30, usage.Days);
        Assert.Equal(Now, usage.GeneratedAtUtc);
        Assert.False(usage.IncludesGnollBenchCalls);
        Assert.False(usage.IncludesEphemeralSessions);
        Assert.True(usage.IncludesSubAgentCalls);
        Assert.False(usage.GuardRefusalsIncludeEncryptedSessions);

        Assert.Equal(AllCorpusTools, usage.Tools.Select(t => t.Tool).ToArray());
        foreach (var tool in usage.Tools)
        {
            Assert.Equal(0, tool.Calls);
            Assert.Equal(0, tool.Completed);
            Assert.Equal(0, tool.Errors);
            Assert.Equal(0, tool.Other);
            Assert.Equal(0, tool.GuardRefusals);
            Assert.Null(tool.P50ExecutionMs);
            Assert.Null(tool.P95ExecutionMs);
            Assert.Equal(30, tool.Daily.Count);
            Assert.All(tool.Daily, count => Assert.Equal(0, count));
        }

        Assert.Equal(
            new[] { CorpusKind.GnollHackWiki, CorpusKind.NetHackWiki, CorpusKind.KnowledgeBase },
            usage.Corpora.Select(c => c.Corpus).ToArray());
        Assert.Equal(new[] { "wiki_search", "wiki_view", "monster_lookup", "item_lookup", "wiki_overview" }, CorpusOf(usage, CorpusKind.GnollHackWiki).Tools);
        Assert.Equal(new[] { "nethack_wiki_search", "nethack_wiki_view", "wiki_overview" }, CorpusOf(usage, CorpusKind.NetHackWiki).Tools);
        Assert.Equal(new[] { "get_knowledge_article" }, CorpusOf(usage, CorpusKind.KnowledgeBase).Tools);
        Assert.All(usage.Corpora, corpus => Assert.Equal(0, corpus.Calls));
    }

    [Fact]
    public async Task GetAsync_CountsAWikiOverviewCallUnderTheWikiItRead()
    {
        var ct = TestContext.Current.CancellationToken;
        DateTime at = Now.AddHours(-1);
        AddCall("wiki_overview", "completed", at, 1);
        AddCall("wiki_overview", "completed", at, 2, argsText: "{\"wiki\":\"gnollhack\"}");
        AddCall("wiki_overview", "error", at, 3, ToolGuardMessages.WikiIndexingInProgress);
        AddCall("wiki_overview", "completed", at, 4, argsText: "{\"wiki\": \"NetHack\"}");
        AddCall("wiki_overview", "error", at, 5, ToolGuardMessages.NetHackWikiIndexingInProgress, "{\"wiki\":\"nethack\"}");
        await SaveAsync();

        var usage = await _service.GetAsync(30, ct);

        var gnollHack = Assert.Single(usage.Tools, t => t.Tool == "wiki_overview" && t.Corpus == CorpusKind.GnollHackWiki);
        Assert.Equal(3, gnollHack.Calls);
        Assert.Equal(2, gnollHack.Completed);
        Assert.Equal(1, gnollHack.GuardRefusals);

        var netHack = Assert.Single(usage.Tools, t => t.Tool == "wiki_overview" && t.Corpus == CorpusKind.NetHackWiki);
        Assert.Equal(2, netHack.Calls);
        Assert.Equal(1, netHack.Completed);
        Assert.Equal(1, netHack.GuardRefusals);

        Assert.Equal(3, CorpusOf(usage, CorpusKind.GnollHackWiki).Calls);
        Assert.Equal(2, CorpusOf(usage, CorpusKind.NetHackWiki).Calls);
    }

    // -- percentiles ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ComputesNearestRankPercentilesOverRecordedTimes()
    {
        var ct = TestContext.Current.CancellationToken;
        DateTime at = Now.AddHours(-2);
        int[] shuffled = { 13, 2, 20, 7, 1, 18, 9, 4, 15, 11, 6, 19, 3, 12, 17, 8, 5, 16, 10, 14 };
        foreach (int ms in shuffled)
        {
            AddCall("wiki_view", "completed", at, ms);
        }
        AddCall("wiki_view", "completed", at, executionMs: null);
        AddCall("monster_lookup", "completed", at, 100);
        AddCall("monster_lookup", "completed", at, 5);
        AddCall("monster_lookup", "completed", at, 50);
        AddCall("item_lookup", "completed", at, 42);
        AddCall("wiki_search", "completed", at, executionMs: null);
        await SaveAsync();

        var usage = await _service.GetAsync(7, ct);

        var wikiView = ToolOf(usage, "wiki_view");
        Assert.Equal(21, wikiView.Calls);
        Assert.Equal(10, wikiView.P50ExecutionMs);
        Assert.Equal(19, wikiView.P95ExecutionMs);

        var monsterLookup = ToolOf(usage, "monster_lookup");
        Assert.Equal(50, monsterLookup.P50ExecutionMs);
        Assert.Equal(100, monsterLookup.P95ExecutionMs);

        var itemLookup = ToolOf(usage, "item_lookup");
        Assert.Equal(42, itemLookup.P50ExecutionMs);
        Assert.Equal(42, itemLookup.P95ExecutionMs);

        var wikiSearch = ToolOf(usage, "wiki_search");
        Assert.Equal(1, wikiSearch.Calls);
        Assert.Null(wikiSearch.P50ExecutionMs);
        Assert.Null(wikiSearch.P95ExecutionMs);

        /* The corpus pools 24 recorded times: 1..20, 5, 42, 50, 100. Rank 12 is 11; rank 23 is 50. */
        var gnollHack = CorpusOf(usage, CorpusKind.GnollHackWiki);
        Assert.Equal(26, gnollHack.Calls);
        Assert.Equal(11, gnollHack.P50ExecutionMs);
        Assert.Equal(50, gnollHack.P95ExecutionMs);
    }

    [Theory]
    [InlineData(new int[0], 50.0, null)]
    [InlineData(new[] { 7 }, 50.0, 7)]
    [InlineData(new[] { 7 }, 95.0, 7)]
    [InlineData(new[] { 1, 2 }, 50.0, 1)]
    [InlineData(new[] { 1, 2 }, 95.0, 2)]
    [InlineData(new[] { 1, 2, 3, 4 }, 50.0, 2)]
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 95.0, 10)]
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 50.0, 5)]
    public void NearestRank_ReturnsTheValueAtCeilingRank(int[] sortedValues, double percentile, int? expected)
    {
        Assert.Equal(expected, CorpusToolUsageService.NearestRank(sortedValues, percentile));
    }

    // -- daily buckets --------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_BucketsCallsByUtcDayAtTheWindowEdges()
    {
        var ct = TestContext.Current.CancellationToken;
        DateTime firstDay = Today.AddDays(-6);
        AddCall("wiki_search", "completed", firstDay.AddTicks(-1));
        AddCall("wiki_search", "completed", firstDay.AddDays(-3));
        AddCall("wiki_search", "completed", firstDay);
        AddCall("wiki_search", "completed", firstDay.AddDays(1).AddTicks(-1));
        AddCall("wiki_search", "completed", firstDay.AddDays(3).AddHours(12));
        AddCall("wiki_search", "completed", Today);
        AddCall("wiki_search", "completed", Now.AddMinutes(-1));
        AddCall("nethack_wiki_search", "completed", Today.AddHours(-1));
        await SaveAsync();

        var usage = await _service.GetAsync(7, ct);

        Assert.Equal(7, usage.Days);
        Assert.Equal(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), usage.SinceUtc);
        Assert.Equal(DateTimeKind.Utc, usage.SinceUtc.Kind);

        var wikiSearch = ToolOf(usage, "wiki_search");
        Assert.Equal(5, wikiSearch.Calls);
        Assert.Equal(new[] { 2, 0, 0, 1, 0, 0, 2 }, wikiSearch.Daily);

        var netHackSearch = ToolOf(usage, "nethack_wiki_search");
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 1, 0 }, netHackSearch.Daily);

        Assert.Equal(new[] { 2, 0, 0, 1, 0, 0, 2 }, CorpusOf(usage, CorpusKind.GnollHackWiki).Daily);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 1, 0 }, CorpusOf(usage, CorpusKind.NetHackWiki).Daily);
    }

    // -- exclusions -----------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_CountsOnlyTheCorpusTools()
    {
        var ct = TestContext.Current.CancellationToken;
        DateTime at = Now.AddHours(-3);
        AddCall("source_code_search", "completed", at, 10);
        AddCall("nethack_source_code_search", "completed", at, 10);
        AddCall("get_item_stats", "completed", at, 10);
        AddCall("delegate_to_agent", "completed", at, 10);
        AddCall("WIKI_SEARCH", "completed", at, 10);
        AddCall("wiki", "completed", at, 10);
        AddCall(null, "completed", at, 10);
        AddCall("wiki_search", "completed", at, 10);
        await SaveAsync();

        var usage = await _service.GetAsync(30, ct);

        Assert.Equal(AllCorpusTools, usage.Tools.Select(t => t.Tool).ToArray());
        Assert.Equal(1, usage.Tools.Sum(t => t.Calls));
        Assert.Equal(1, ToolOf(usage, "wiki_search").Calls);
        Assert.Equal(1, usage.Corpora.Sum(c => c.Calls));
        Assert.Equal(1, CorpusOf(usage, CorpusKind.GnollHackWiki).Calls);
    }

    // -- days -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(7, 7)]
    [InlineData(30, 30)]
    [InlineData(90, 90)]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(14, 30)]
    [InlineData(-7, 30)]
    [InlineData(365, 30)]
    public async Task GetAsync_ClampsDaysToSevenThirtyOrNinety(int requested, int expected)
    {
        var ct = TestContext.Current.CancellationToken;

        var usage = await _service.GetAsync(requested, ct);

        Assert.Equal(expected, usage.Days);
        Assert.Equal(Today.AddDays(-(expected - 1)), usage.SinceUtc);
        Assert.All(usage.Tools, tool => Assert.Equal(expected, tool.Daily.Count));
        Assert.All(usage.Corpora, corpus => Assert.Equal(expected, corpus.Daily.Count));
        Assert.Equal(expected, CorpusToolUsageService.ClampDays(requested));
    }

    [Fact]
    public async Task GetAsync_NinetyDays_IncludesACallEightyNineDaysBack()
    {
        var ct = TestContext.Current.CancellationToken;
        AddCall("get_knowledge_article", "completed", Today.AddDays(-89).AddHours(1));
        AddCall("get_knowledge_article", "completed", Today.AddDays(-90).AddHours(23));
        await SaveAsync();

        var ninety = await _service.GetAsync(90, ct);
        var thirty = await _service.GetAsync(30, ct);

        var article = ToolOf(ninety, "get_knowledge_article");
        Assert.Equal(1, article.Calls);
        Assert.Equal(1, article.Daily[0]);
        Assert.Equal(0, ToolOf(thirty, "get_knowledge_article").Calls);
    }

    // -- cache ----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_CachesEachPeriodSeparatelyWithSizeOne()
    {
        var ct = TestContext.Current.CancellationToken;
        AddCall("wiki_search", "completed", Now.AddHours(-1), 10);
        await SaveAsync();

        var first = await _service.GetAsync(7, ct);
        AddCall("wiki_search", "completed", Now.AddHours(-1), 10);
        await SaveAsync();
        var second = await _service.GetAsync(7, ct);
        var clampedToThirty = await _service.GetAsync(14, ct);
        var thirty = await _service.GetAsync(30, ct);

        Assert.Same(first, second);
        Assert.Equal(1, ToolOf(second, "wiki_search").Calls);
        Assert.NotSame(first, clampedToThirty);
        Assert.Same(clampedToThirty, thirty);
        Assert.Equal(2, ToolOf(thirty, "wiki_search").Calls);

        Assert.Equal(2, _cache.Count);
        var statistics = _cache.GetCurrentStatistics();
        Assert.NotNull(statistics);
        Assert.Equal(2L, statistics!.CurrentEstimatedSize);
    }
}
