using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MobileGnollHackLogger.Data;
using Overseer.Services.Tools;

namespace Overseer.Services.CorpusIndexing;

/// <summary>A chat tool that reads one corpus.</summary>
public sealed record CorpusTool(string Name, CorpusKind Corpus);

/// <summary>Call counts and timings of one tool, or of a corpus's tools together.</summary>
public abstract class CorpusToolUsageStatsDto
{
    public int Calls { get; init; }

    /// <summary>Calls with status <c>completed</c>.</summary>
    public int Completed { get; init; }

    /// <summary>Calls with status <c>error</c>, guard refusals included.</summary>
    public int Errors { get; init; }

    /// <summary>Calls with any other status, such as <c>canceled</c>, or none.</summary>
    public int Other { get; init; }

    /// <summary>Calls refused because the corpus index was not ready: the error is the corpus's <see cref="ToolGuardMessages"/> text.</summary>
    public int GuardRefusals { get; init; }

    /// <summary>The nearest-rank median of the recorded execution times; null when none is recorded.</summary>
    public int? P50ExecutionMs { get; init; }

    /// <summary>The nearest-rank 95th percentile of the recorded execution times; null when none is recorded.</summary>
    public int? P95ExecutionMs { get; init; }

    /// <summary>Calls per UTC day, one entry per day of the period, oldest first.</summary>
    public IReadOnlyList<int> Daily { get; init; } = Array.Empty<int>();
}

/// <summary>The usage of one tool.</summary>
public sealed class CorpusToolUsageToolDto : CorpusToolUsageStatsDto
{
    public string Tool { get; init; } = string.Empty;
    public CorpusKind Corpus { get; init; }
}

/// <summary>The usage of a corpus's tools together.</summary>
public sealed class CorpusToolUsageCorpusDto : CorpusToolUsageStatsDto
{
    public CorpusKind Corpus { get; init; }
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();
}

/// <summary>How the chat used the corpus tools over a period of whole UTC days ending today.</summary>
public sealed class CorpusToolUsageDto
{
    /// <summary>7, 30 or 90.</summary>
    public int Days { get; init; }

    /// <summary>Midnight UTC of the period's first day; the period ends now.</summary>
    public DateTime SinceUtc { get; init; }

    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>Always false: GnollBench calls are stored apart, in <c>BenchmarkRunAnswerToolCalls</c>.</summary>
    public bool IncludesGnollBenchCalls { get; init; }

    /// <summary>Always false: ephemeral sessions are never stored.</summary>
    public bool IncludesEphemeralSessions { get; init; }

    /// <summary>Always true: calls made by delegated sub-agents are stored with the turn and counted.</summary>
    public bool IncludesSubAgentCalls { get; init; }

    /// <summary>
    /// Always false: a confidential session stored encrypted keeps its error text as ciphertext, so a
    /// guard refusal there counts as an error but not as a guard refusal.
    /// </summary>
    public bool GuardRefusalsIncludeEncryptedSessions { get; init; }

    /// <summary>One entry per tool of <see cref="CorpusToolUsageService.CorpusTools"/>, in that order, unused tools included.</summary>
    public IReadOnlyList<CorpusToolUsageToolDto> Tools { get; init; } = Array.Empty<CorpusToolUsageToolDto>();

    /// <summary>One entry per corpus, in <see cref="CorpusKind"/> order.</summary>
    public IReadOnlyList<CorpusToolUsageCorpusDto> Corpora { get; init; } = Array.Empty<CorpusToolUsageCorpusDto>();
}

/// <summary>
/// Aggregates the chat's stored calls of the corpus tools from <c>ChatMessageToolCall</c>, dated by
/// their message's <c>TimestampUtc</c>. It reads only each call's name, status, execution time and
/// whether its error is a guard message; never its arguments, its result or the session's user.
/// A result is cached per period for 5 minutes.
/// </summary>
public class CorpusToolUsageService
{
    public const int DefaultDays = 30;

    /// <summary>The periods <see cref="GetAsync"/> accepts; any other value becomes <see cref="DefaultDays"/>.</summary>
    public static readonly IReadOnlyList<int> AllowedDays = new[] { 7, 30, 90 };

    /// <summary>The corpus tools, grouped by corpus in <see cref="CorpusKind"/> order.</summary>
    public static readonly IReadOnlyList<CorpusTool> CorpusTools = new[]
    {
        new CorpusTool("wiki_search", CorpusKind.GnollHackWiki),
        new CorpusTool("wiki_view", CorpusKind.GnollHackWiki),
        new CorpusTool("monster_lookup", CorpusKind.GnollHackWiki),
        new CorpusTool("item_lookup", CorpusKind.GnollHackWiki),
        new CorpusTool("nethack_wiki_search", CorpusKind.NetHackWiki),
        new CorpusTool("nethack_wiki_view", CorpusKind.NetHackWiki),
        new CorpusTool("get_knowledge_article", CorpusKind.KnowledgeBase)
    };

    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private const string CacheKeyPrefix = "Overseer.CorpusToolUsage:";
    private const int NoGuard = -1;

    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;

    public CorpusToolUsageService(ApplicationDbContext dbContext, IMemoryCache cache, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _cache = cache;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The names of <paramref name="corpus"/>'s tools, in <see cref="CorpusTools"/> order.</summary>
    public static IReadOnlyList<string> ToolsOf(CorpusKind corpus) =>
        CorpusTools.Where(t => t.Corpus == corpus).Select(t => t.Name).ToList();

    /// <summary>The <see cref="ToolGuardMessages"/> text a tool of <paramref name="corpus"/> returns while the index is not ready.</summary>
    public static string GuardMessageOf(CorpusKind corpus) => corpus switch
    {
        CorpusKind.GnollHackWiki => ToolGuardMessages.WikiIndexingInProgress,
        CorpusKind.NetHackWiki => ToolGuardMessages.NetHackWikiIndexingInProgress,
        CorpusKind.KnowledgeBase => ToolGuardMessages.KnowledgeBaseIndexingInProgress,
        _ => throw new ArgumentOutOfRangeException(nameof(corpus), corpus, null)
    };

    /// <summary><paramref name="days"/> when it is 7, 30 or 90; otherwise <see cref="DefaultDays"/>.</summary>
    public static int ClampDays(int days) => AllowedDays.Contains(days) ? days : DefaultDays;

    /// <summary>The usage over the last <paramref name="days"/> UTC days, today included; see <see cref="ClampDays"/>.</summary>
    public async Task<CorpusToolUsageDto> GetAsync(int days, CancellationToken cancellationToken)
    {
        days = ClampDays(days);
        string cacheKey = CacheKeyPrefix + days;
        if (_cache.TryGetValue(cacheKey, out CorpusToolUsageDto? cached) && cached != null)
        {
            return cached;
        }

        DateTime now = _time.GetUtcNow().UtcDateTime;
        DateTime firstDay = DateTime.SpecifyKind(now.Date.AddDays(-(days - 1)), DateTimeKind.Utc);
        List<string> toolNames = CorpusTools.Select(t => t.Name).ToList();

        var rows = await _dbContext.ChatMessageToolCall
            .AsNoTracking()
            .Where(tc => tc.Name != null && toolNames.Contains(tc.Name) && tc.ChatMessage!.TimestampUtc >= firstDay)
            .Select(tc => new UsageRow
            {
                Name = tc.Name!,
                Status = tc.Status,
                GuardCorpus = tc.Error == ToolGuardMessages.WikiIndexingInProgress ? (int)CorpusKind.GnollHackWiki
                    : tc.Error == ToolGuardMessages.NetHackWikiIndexingInProgress ? (int)CorpusKind.NetHackWiki
                    : tc.Error == ToolGuardMessages.KnowledgeBaseIndexingInProgress ? (int)CorpusKind.KnowledgeBase
                    : NoGuard,
                ExecutionMs = tc.ExecutionMs,
                TimestampUtc = tc.ChatMessage!.TimestampUtc
            })
            .ToListAsync(cancellationToken);

        var result = Aggregate(rows, days, firstDay, now);
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1
        });
        return result;
    }

    private static CorpusToolUsageDto Aggregate(List<UsageRow> rows, int days, DateTime firstDay, DateTime now)
    {
        var corpusOf = CorpusTools.ToDictionary(t => t.Name, t => t.Corpus, StringComparer.Ordinal);
        var perTool = CorpusTools.ToDictionary(t => t.Name, _ => new StatsAccumulator(days), StringComparer.Ordinal);
        var perCorpus = Enum.GetValues<CorpusKind>().ToDictionary(c => c, _ => new StatsAccumulator(days));

        foreach (var row in rows)
        {
            if (!corpusOf.TryGetValue(row.Name, out var corpus)) continue;

            int day = Math.Clamp((row.TimestampUtc.Date - firstDay).Days, 0, days - 1);
            bool guard = row.GuardCorpus == (int)corpus;
            perTool[row.Name].Add(row, day, guard);
            perCorpus[corpus].Add(row, day, guard);
        }

        return new CorpusToolUsageDto
        {
            Days = days,
            SinceUtc = firstDay,
            GeneratedAtUtc = now,
            IncludesGnollBenchCalls = false,
            IncludesEphemeralSessions = false,
            IncludesSubAgentCalls = true,
            GuardRefusalsIncludeEncryptedSessions = false,
            Tools = CorpusTools.Select(t => perTool[t.Name].ToToolDto(t)).ToList(),
            Corpora = perCorpus.OrderBy(p => p.Key).Select(p => p.Value.ToCorpusDto(p.Key, ToolsOf(p.Key))).ToList()
        };
    }

    /// <summary>The value at nearest rank ceil(p/100 * n) of <paramref name="sortedValues"/>; null when it is empty.</summary>
    internal static int? NearestRank(IReadOnlyList<int> sortedValues, double percentile)
    {
        int n = sortedValues.Count;
        if (n == 0) return null;
        int rank = (int)Math.Ceiling(percentile / 100.0 * n);
        return sortedValues[Math.Clamp(rank, 1, n) - 1];
    }

    /// <summary>The columns read per call.</summary>
    private sealed class UsageRow
    {
        public string Name { get; init; } = string.Empty;
        public string? Status { get; init; }

        /// <summary>The <see cref="CorpusKind"/> whose guard message the error equals, or -1.</summary>
        public int GuardCorpus { get; init; }

        public int? ExecutionMs { get; init; }
        public DateTime TimestampUtc { get; init; }
    }

    private sealed class StatsAccumulator
    {
        private readonly int[] _daily;
        private readonly List<int> _executionMs = new();
        private int _calls;
        private int _completed;
        private int _errors;
        private int _other;
        private int _guardRefusals;

        public StatsAccumulator(int days)
        {
            _daily = new int[days];
        }

        public void Add(UsageRow row, int day, bool guard)
        {
            _calls++;
            _daily[day]++;
            switch (row.Status)
            {
                case "completed": _completed++; break;
                case "error": _errors++; break;
                default: _other++; break;
            }
            if (guard) _guardRefusals++;
            if (row.ExecutionMs is int ms) _executionMs.Add(ms);
        }

        public CorpusToolUsageToolDto ToToolDto(CorpusTool tool)
        {
            _executionMs.Sort();
            return new CorpusToolUsageToolDto
            {
                Tool = tool.Name,
                Corpus = tool.Corpus,
                Calls = _calls,
                Completed = _completed,
                Errors = _errors,
                Other = _other,
                GuardRefusals = _guardRefusals,
                P50ExecutionMs = NearestRank(_executionMs, 50),
                P95ExecutionMs = NearestRank(_executionMs, 95),
                Daily = _daily.ToArray()
            };
        }

        public CorpusToolUsageCorpusDto ToCorpusDto(CorpusKind corpus, IReadOnlyList<string> tools)
        {
            _executionMs.Sort();
            return new CorpusToolUsageCorpusDto
            {
                Corpus = corpus,
                Tools = tools,
                Calls = _calls,
                Completed = _completed,
                Errors = _errors,
                Other = _other,
                GuardRefusals = _guardRefusals,
                P50ExecutionMs = NearestRank(_executionMs, 50),
                P95ExecutionMs = NearestRank(_executionMs, 95),
                Daily = _daily.ToArray()
            };
        }
    }
}
