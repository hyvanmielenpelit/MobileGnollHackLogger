namespace Overseer.Services.Telemetry;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;

/// <summary>
/// The AI Telemetry tab's queries: the summary and breakdown, the series over time, and per-user usage of system
/// AI configurations. Reads <see cref="SystemAiUsageLog"/>, <see cref="UserKeyAiUsageLog"/>,
/// <see cref="ChatProviderAttempt"/>, <see cref="OverseerDailyActivity"/> and, for refusal counts,
/// <see cref="SecurityEvent"/>. Token figures are normalized per role context (<see cref="AiUsageRoles"/>).
/// </summary>
public class AiTelemetrySummaryService
{
    public const int MaxSeriesBuckets = 1100;
    public const int MaxSeriesLines = 8;
    public const string OtherLineKey = "other";

    private readonly ApplicationDbContext _db;
    private readonly ModelMetadataService _metadata;

    public AiTelemetrySummaryService(ApplicationDbContext db, ModelMetadataService metadata)
    {
        _db = db;
        _metadata = metadata;
    }

    /// <summary>system | user | both; anything else is system.</summary>
    public static string NormalizeKeySource(string? keySource) => keySource?.Trim().ToLowerInvariant() switch
    {
        "user" => "user",
        "both" => "both",
        _ => "system"
    };

    private static bool IncludesSystem(string keys) => keys is "system" or "both";
    private static bool IncludesUser(string keys) => keys is "user" or "both";

    private static IQueryable<SystemAiUsageLog> InRange(IQueryable<SystemAiUsageLog> q, DateTime? start, DateTime? end)
    {
        if (start.HasValue) { var from = start.Value; q = q.Where(l => l.TimestampUtc >= from); }
        if (end.HasValue) { var to = end.Value.AddDays(1); q = q.Where(l => l.TimestampUtc < to); }
        return q;
    }

    private static IQueryable<UserKeyAiUsageLog> InRange(IQueryable<UserKeyAiUsageLog> q, DateTime? start, DateTime? end)
    {
        if (start.HasValue) { var from = start.Value; q = q.Where(l => l.TimestampUtc >= from); }
        if (end.HasValue) { var to = end.Value.AddDays(1); q = q.Where(l => l.TimestampUtc < to); }
        return q;
    }

    private static IQueryable<ChatProviderAttempt> InRange(IQueryable<ChatProviderAttempt> q, DateTime? start, DateTime? end)
    {
        if (start.HasValue) { var from = start.Value; q = q.Where(a => a.StartedAtUtc >= from); }
        if (end.HasValue) { var to = end.Value.AddDays(1); q = q.Where(a => a.StartedAtUtc < to); }
        return q;
    }

    private static IQueryable<OverseerDailyActivity> InRange(IQueryable<OverseerDailyActivity> q, DateTime? start, DateTime? end)
    {
        if (start.HasValue) { var from = DateOnly.FromDateTime(start.Value); q = q.Where(a => a.DateUtc >= from); }
        if (end.HasValue) { var to = DateOnly.FromDateTime(end.Value); q = q.Where(a => a.DateUtc <= to); }
        return q;
    }

    /// <summary>Sums of one group of usage rows.</summary>
    private sealed class UsageAggregate
    {
        public string KeySource { get; init; } = "system";
        public string? UserId { get; init; }
        public string Provider { get; init; } = string.Empty;
        public string ModelId { get; init; } = string.Empty;
        public int RoleContext { get; init; }
        public string? ThinkingLevel { get; init; }
        public long Requests { get; init; }
        public long Input { get; init; }
        public long Output { get; init; }
        public long CacheRead { get; init; }
        public long CacheWrite { get; init; }
        public decimal Cost { get; init; }
        public long Priced { get; init; }
        public long DurationSum { get; init; }
        public long DurationCount { get; init; }
    }

    /// <summary>The ledger columns the summary and series read.</summary>
    private sealed record AttemptRow(DateTime StartedAtUtc, Guid AgentRunId, int RoleContext, byte AgentDepth, AiKeySource KeySource,
        string? UserId, string Provider, string ModelId, string ThinkingLevel, int CallIndex, byte AttemptIndex,
        ProviderAttemptOutcome Outcome, bool Retried);

    // --- Summary ---------------------------------------------------------------------------------

    public async Task<AiTelemetrySummaryDto> GetSummaryAsync(DateTime? start, DateTime? end, string? keySource, CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        var aggregates = new List<UsageAggregate>();
        DateTime? costSince = null;

        if (IncludesSystem(keys))
        {
            var q = InRange(_db.SystemAiUsageLogs.AsNoTracking(), start, end);
            aggregates.AddRange((await q
                .GroupBy(l => new { l.Provider, l.ModelId, l.RoleContext, l.ThinkingLevel })
                .Select(g => new
                {
                    g.Key.Provider, g.Key.ModelId, g.Key.RoleContext, g.Key.ThinkingLevel,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    CacheWrite = g.Sum(x => (long)(x.CacheCreationInputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    DurationSum = g.Sum(x => (long)(x.TotalDurationMs ?? 0)),
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null)
                })
                .ToListAsync(ct))
                .Select(a => new UsageAggregate
                {
                    KeySource = "system", Provider = a.Provider, ModelId = a.ModelId, RoleContext = a.RoleContext,
                    ThinkingLevel = a.ThinkingLevel, Requests = a.Requests, Input = a.Input, Output = a.Output,
                    CacheRead = a.CacheRead, CacheWrite = a.CacheWrite, Cost = a.Cost, Priced = a.Priced,
                    DurationSum = a.DurationSum, DurationCount = a.DurationCount
                }));
            costSince = Earliest(costSince, await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct));
        }

        if (IncludesUser(keys))
        {
            var q = InRange(_db.UserKeyAiUsageLogs.AsNoTracking(), start, end);
            aggregates.AddRange((await q
                .GroupBy(l => new { l.AspNetUserId, l.Provider, l.ModelId, l.RoleContext, l.ThinkingLevel })
                .Select(g => new
                {
                    g.Key.AspNetUserId, g.Key.Provider, g.Key.ModelId, g.Key.RoleContext, g.Key.ThinkingLevel,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    CacheWrite = g.Sum(x => (long)(x.CacheCreationInputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    DurationSum = g.Sum(x => (long)(x.TotalDurationMs ?? 0)),
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null)
                })
                .ToListAsync(ct))
                .Select(a => new UsageAggregate
                {
                    KeySource = "user", UserId = a.AspNetUserId, Provider = a.Provider, ModelId = a.ModelId,
                    RoleContext = a.RoleContext, ThinkingLevel = a.ThinkingLevel, Requests = a.Requests, Input = a.Input,
                    Output = a.Output, CacheRead = a.CacheRead, CacheWrite = a.CacheWrite, Cost = a.Cost, Priced = a.Priced,
                    DurationSum = a.DurationSum, DurationCount = a.DurationCount
                }));
            costSince = Earliest(costSince, await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct));
        }

        var rows = aggregates.Select(ToRow).ToList();

        var attempts = await LoadAttemptsAsync(start, end, keys, userId: null, ct);
        MergeAttempts(rows, attempts);

        await FillUserNamesAsync(rows, ct);
        FillModelNames(rows);

        rows = rows
            .OrderByDescending(r => r.Requests)
            .ThenBy(r => r.ModelName ?? r.ModelId, StringComparer.Ordinal)
            .ThenBy(r => r.ThinkingLevel ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        long prompt = rows.Sum(r => r.PromptTokens);
        long read = rows.Sum(r => r.CacheReadTokens);
        long durationSum = rows.Sum(r => r.DurationSumMs);
        long durationCount = rows.Sum(r => r.DurationCount);

        return new AiTelemetrySummaryDto
        {
            KeySource = keys,
            TotalRequests = rows.Sum(r => r.Requests),
            TotalChatRequests = rows.Where(r => r.RoleGroup == AiUsageRoles.Chat).Sum(r => r.Requests),
            TotalTitleRequests = rows.Where(r => r.RoleGroup == AiUsageRoles.TitleGeneration).Sum(r => r.Requests),
            TotalBenchmarkRequests = rows.Where(r => r.RoleGroup == AiUsageRoles.Benchmark).Sum(r => r.Requests),
            TotalPromptTokens = prompt,
            TotalUncachedInputTokens = rows.Sum(r => r.UncachedInputTokens),
            TotalOutputTokens = rows.Sum(r => r.OutputTokens),
            TotalCacheReadTokens = read,
            TotalCacheCreationTokens = rows.Sum(r => r.CacheCreationTokens),
            CacheHitRatio = prompt > 0 ? Math.Round((double)read / prompt, 4) : 0.0,
            AvgDurationMs = durationCount > 0 ? (int)(durationSum / durationCount) : 0,
            TotalEstimatedCost = rows.Sum(r => r.EstimatedCost),
            PricedRequests = rows.Sum(r => r.PricedRequests),
            CostRecordedSinceUtc = Utc(costSince),
            Rows = rows,
            ChatAvailability = await BuildAvailabilityAsync(attempts, ct),
            Activity = await BuildActivityAsync(start, end, ct)
        };
    }

    private static AiUsageBreakdownRowDto ToRow(UsageAggregate a) => new()
    {
        KeySource = a.KeySource,
        UserId = a.UserId,
        Provider = a.Provider,
        ModelId = a.ModelId,
        RoleContext = a.RoleContext,
        RoleGroup = AiUsageRoles.GroupOf(a.RoleContext),
        ThinkingLevel = a.ThinkingLevel,
        Requests = a.Requests,
        PromptTokens = AiUsageRoles.PromptTokens(a.RoleContext, a.Input, a.CacheRead),
        UncachedInputTokens = AiUsageRoles.UncachedInputTokens(a.RoleContext, a.Input, a.CacheRead, a.CacheWrite),
        CacheReadTokens = a.CacheRead,
        CacheCreationTokens = a.CacheWrite,
        OutputTokens = a.Output,
        DurationSumMs = a.DurationSum,
        DurationCount = a.DurationCount,
        EstimatedCost = a.Cost,
        PricedRequests = a.Priced
    };

    private async Task<List<AttemptRow>> LoadAttemptsAsync(DateTime? start, DateTime? end, string keys, string? userId, CancellationToken ct)
    {
        var q = InRange(_db.ChatProviderAttempts.AsNoTracking(), start, end);
        if (!IncludesSystem(keys)) q = q.Where(a => a.KeySource == AiKeySource.UserKey);
        if (!IncludesUser(keys)) q = q.Where(a => a.KeySource == AiKeySource.System);
        if (userId != null) q = q.Where(a => a.AspNetUserId == userId);

        return await q
            .Select(a => new AttemptRow(a.StartedAtUtc, a.AgentRunId, a.RoleContext, a.AgentDepth, a.KeySource, a.AspNetUserId,
                a.Provider, a.RequestedModelId, a.ThinkingLevel, a.CallIndex, a.AttemptIndex, a.Outcome, a.Retried))
            .ToListAsync(ct);
    }

    /// <summary>Fills each row's attempt figures from the ledger; a model that only failed gets a row with no requests.</summary>
    private static void MergeAttempts(List<AiUsageBreakdownRowDto> rows, List<AttemptRow> attempts)
    {
        var groups = attempts.GroupBy(a => (
            Key: a.KeySource == AiKeySource.UserKey ? "user" : "system",
            UserId: a.KeySource == AiKeySource.UserKey ? a.UserId : null,
            a.Provider, a.ModelId, a.RoleContext, a.ThinkingLevel));

        foreach (var g in groups)
        {
            var row = rows.FirstOrDefault(r => r.KeySource == g.Key.Key && r.UserId == g.Key.UserId
                && string.Equals(r.Provider, g.Key.Provider, StringComparison.Ordinal)
                && string.Equals(r.ModelId, g.Key.ModelId, StringComparison.Ordinal)
                && r.RoleContext == g.Key.RoleContext
                && string.Equals(r.ThinkingLevel ?? string.Empty, g.Key.ThinkingLevel, StringComparison.Ordinal));

            if (row == null)
            {
                row = new AiUsageBreakdownRowDto
                {
                    KeySource = g.Key.Key,
                    UserId = g.Key.UserId,
                    Provider = g.Key.Provider,
                    ModelId = g.Key.ModelId,
                    RoleContext = g.Key.RoleContext,
                    RoleGroup = AiUsageRoles.GroupOf(g.Key.RoleContext),
                    ThinkingLevel = g.Key.ThinkingLevel
                };
                rows.Add(row);
            }

            row.ValidAttempts += g.Count(a => ProviderAttemptOutcomes.IsValid(a.Outcome));
            row.ProviderErrors += g.Count(a => ProviderAttemptOutcomes.IsProvider(a.Outcome));
            row.UnattributedErrors += g.Count(a => ProviderAttemptOutcomes.IsUnattributed(a.Outcome));
            row.OurSideFailures += g.Count(a => ProviderAttemptOutcomes.IsOurs(a.Outcome));
        }
    }

    private async Task FillUserNamesAsync(List<AiUsageBreakdownRowDto> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.UserId != null).Select(r => r.UserId!).Distinct().ToList();
        if (ids.Count == 0) return;
        var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
        foreach (var row in rows.Where(r => r.UserId != null))
        {
            row.UserName = names.TryGetValue(row.UserId!, out var name) ? name : null;
        }
    }

    private void FillModelNames(List<AiUsageBreakdownRowDto> rows)
    {
        var cache = new Dictionary<(string, string), string?>();
        foreach (var row in rows)
        {
            row.ModelName = ModelName(row.Provider, row.ModelId, cache);
        }
    }

    /// <summary>The catalog name, else the retired entry's, else null.</summary>
    private string? ModelName(string provider, string modelId, Dictionary<(string, string), string?> cache)
    {
        if (cache.TryGetValue((provider, modelId), out var known)) return known;
        string? name = _metadata.GetMetadata(provider, modelId).DisplayName;
        if (string.IsNullOrEmpty(name)) name = _metadata.GetRetiredEntry(provider, modelId)?.DisplayName;
        if (string.IsNullOrEmpty(name)) name = null;
        cache[(provider, modelId)] = name;
        return name;
    }

    private async Task<ChatProviderAvailabilityDto> BuildAvailabilityAsync(List<AttemptRow> attempts, CancellationToken ct)
    {
        var dto = new ChatProviderAvailabilityDto();
        dto.RecordedSinceUtc = Utc(await _db.ChatProviderAttempts.MinAsync(a => (DateTime?)a.StartedAtUtc, ct));
        dto.Recorded = dto.RecordedSinceUtc != null;

        dto.ValidAttempts = attempts.Count(a => ProviderAttemptOutcomes.IsValid(a.Outcome));
        dto.ProviderErrors = attempts.Count(a => ProviderAttemptOutcomes.IsProvider(a.Outcome));
        dto.UnattributedErrors = attempts.Count(a => ProviderAttemptOutcomes.IsUnattributed(a.Outcome));
        dto.OurSideFailures = attempts.Count(a => ProviderAttemptOutcomes.IsOurs(a.Outcome));
        dto.ProviderErrorRate = dto.ValidAttempts > 0 ? (double)dto.ProviderErrors / dto.ValidAttempts : null;
        dto.InclusiveRate = dto.ValidAttempts > 0 ? (double)(dto.ProviderErrors + dto.UnattributedErrors) / dto.ValidAttempts : null;

        dto.Overloaded = attempts.Count(a => a.Outcome == ProviderAttemptOutcome.ProviderOverloaded);
        dto.ServerErrors = attempts.Count(a => a.Outcome == ProviderAttemptOutcome.ProviderServerError);
        dto.Timeouts = attempts.Count(a => a.Outcome == ProviderAttemptOutcome.ProviderTimeout);
        dto.StreamFailures = attempts.Count(a => a.Outcome == ProviderAttemptOutcome.ProviderStreamFailure);
        dto.RateLimitedUnattributed = attempts.Count(a => a.Outcome == ProviderAttemptOutcome.RateLimitUnattributed);
        dto.TransportUnattributed = attempts.Count(a => a.Outcome == ProviderAttemptOutcome.TransportUnattributed);

        if (dto.ValidAttempts > 0)
        {
            // GnollBench's interval and block width: one cluster per 10-minute UTC block.
            int blockMinutes = (int)BenchmarkProviderAvailability.ClusterBlockWidth.TotalMinutes;
            var clusters = attempts
                .Where(a => ProviderAttemptOutcomes.IsValid(a.Outcome))
                .GroupBy(a => a.StartedAtUtc.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + "-" + (a.StartedAtUtc.Minute / blockMinutes))
                .Select(g => (Cluster: g.Key, K: g.Count(a => ProviderAttemptOutcomes.IsProvider(a.Outcome)), N: g.Count()));
            var (low, high, _) = BenchmarkProviderAvailability.ClusterAdjustedWilson95(clusters);
            dto.Low = low;
            dto.High = high;
        }

        dto.Calls = attempts.Count(a => a.AttemptIndex == 0);
        dto.FirstAttemptSuccessCalls = attempts.Count(a => a.AttemptIndex == 0 && a.Outcome == ProviderAttemptOutcome.Ok);
        dto.FailedCalls = attempts.Count(a => !a.Retried && a.Outcome != ProviderAttemptOutcome.Ok);

        var turns = attempts.Where(a => a.RoleContext == 1 && a.AgentDepth == 0).GroupBy(a => a.AgentRunId).ToList();
        dto.ChatTurns = turns.Count;
        dto.ChatTurnsEndedByProviderError = turns.Count(t =>
        {
            var last = t.OrderBy(a => a.CallIndex).ThenBy(a => a.AttemptIndex).Last();
            return last.Outcome != ProviderAttemptOutcome.Ok && ProviderAttemptOutcomes.IsProvider(last.Outcome);
        });

        var titles = attempts.Where(a => a.RoleContext == 2).ToList();
        dto.TitleValidAttempts = titles.Count(a => ProviderAttemptOutcomes.IsValid(a.Outcome));
        dto.TitleProviderErrors = titles.Count(a => ProviderAttemptOutcomes.IsProvider(a.Outcome));
        dto.TitleRequests = titles.Count(a => a.AttemptIndex == 0);
        dto.TitleRequestsFailed = titles.Count(a => !a.Retried && a.Outcome != ProviderAttemptOutcome.Ok);
        return dto;
    }

    private async Task<OverseerActivityDto> BuildActivityAsync(DateTime? start, DateTime? end, CancellationToken ct)
    {
        var rows = await InRange(_db.OverseerDailyActivities.AsNoTracking(), start, end).ToListAsync(ct);
        var dto = new OverseerActivityDto
        {
            ChatsCreated = rows.Sum(r => r.ChatsCreated),
            ConfidentialChatsCreated = rows.Sum(r => r.ConfidentialChatsCreated),
            GnollHackChatsCreated = rows.Sum(r => r.GnollHackChatsCreated),
            UserMessages = rows.Sum(r => r.UserMessages),
            AssistantMessages = rows.Sum(r => r.AssistantMessages),
            IncognitoChatsCreated = rows.Sum(r => r.IncognitoChatsCreated),
            IncognitoUserMessages = rows.Sum(r => r.IncognitoUserMessages),
            CountedSinceUtc = await _db.OverseerDailyActivities.MinAsync(a => (DateOnly?)a.DateUtc, ct)
        };

        var active = rows.Where(r => r.AspNetUserId != null && r.ChatsCreated + r.UserMessages > 0)
            .Select(r => r.AspNetUserId!).Distinct().ToList();
        dto.ActiveUsers = active.Count;

        if (active.Count > 0)
        {
            var firstDays = await FirstActivityDaysAsync(active, ct);
            DateOnly? from = start.HasValue ? DateOnly.FromDateTime(start.Value) : null;
            DateOnly? to = end.HasValue ? DateOnly.FromDateTime(end.Value) : null;
            dto.NewUsers = active.Count(id => firstDays.TryGetValue(id, out var first)
                && (from == null || first >= from) && (to == null || first <= to));
        }
        return dto;
    }

    /// <summary>Each user's first day with any activity, over the whole table.</summary>
    private async Task<Dictionary<string, DateOnly>> FirstActivityDaysAsync(List<string> userIds, CancellationToken ct) =>
        await _db.OverseerDailyActivities.AsNoTracking()
            .Where(a => a.AspNetUserId != null && userIds.Contains(a.AspNetUserId) && a.ChatsCreated + a.UserMessages > 0)
            .GroupBy(a => a.AspNetUserId!)
            .Select(g => new { UserId = g.Key, First = g.Min(a => a.DateUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.First, ct);

    // --- Series ----------------------------------------------------------------------------------

    /// <summary>auto: day up to 45 days, week up to 180, else month.</summary>
    public static string ResolveBucket(DateTime start, DateTime end, string? bucket)
    {
        string b = bucket?.Trim().ToLowerInvariant() ?? "auto";
        if (b is "day" or "week" or "month") return b;
        int days = (int)(end.Date - start.Date).TotalDays + 1;
        return days <= 45 ? "day" : days <= 180 ? "week" : "month";
    }

    /// <summary>The bucket start containing <paramref name="t"/>: the day, the Monday of its week, or the 1st of its month (UTC).</summary>
    public static DateTime BucketStart(DateTime t, string bucket)
    {
        var day = t.Date;
        return bucket switch
        {
            "week" => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            "month" => new DateTime(day.Year, day.Month, 1),
            _ => day
        };
    }

    public static List<DateTime> BucketStarts(DateTime start, DateTime end, string bucket)
    {
        var list = new List<DateTime>();
        var cursor = BucketStart(start, bucket);
        var last = end.Date;
        while (cursor <= last && list.Count < MaxSeriesBuckets)
        {
            list.Add(DateTime.SpecifyKind(cursor, DateTimeKind.Utc));
            cursor = bucket switch
            {
                "week" => cursor.AddDays(7),
                "month" => cursor.AddMonths(1),
                _ => cursor.AddDays(1)
            };
        }
        return list;
    }

    /// <summary>How many buckets a series over the range has, with <c>auto</c> resolved. Used by the range guard.</summary>
    public static int BucketCount(DateTime start, DateTime end, string? bucket)
    {
        if (end < start) (start, end) = (end, start);
        string resolved = ResolveBucket(start, end, bucket);
        int days = (int)(end.Date - start.Date).TotalDays + 1;
        return resolved switch
        {
            "week" => (int)((BucketStart(end, "week") - BucketStart(start, "week")).TotalDays / 7) + 1,
            "month" => (end.Year - start.Year) * 12 + end.Month - start.Month + 1,
            _ => days
        };
    }

    /// <summary>The range a series covers: the requested dates, else the last 30 days ending today.</summary>
    public static (DateTime Start, DateTime End) SeriesRange(DateTime? start, DateTime? end)
    {
        var e = (end ?? DateTime.UtcNow).Date;
        var s = (start ?? e.AddDays(-29)).Date;
        if (e < s) (s, e) = (e, s);
        return (s, e);
    }

    /// <summary>One usage row as the series folds it.</summary>
    private sealed record SeriesUsage(DateTime Day, string KeySource, string? UserId, string Provider, string ModelId, int RoleContext,
        long Requests, long Input, long Output, long CacheRead, decimal Cost, long Priced, long DurationSum, long DurationCount);

    /// <param name="roles">Role groups to include; null or empty: the three named groups.</param>
    /// <param name="splitBy">none | role | provider | model | keySource | user.</param>
    public async Task<AiTelemetrySeriesDto> GetSeriesAsync(DateTime? start, DateTime? end, string? keySource, IReadOnlyCollection<string>? roles,
        string? bucket, string? splitBy, string? userId, CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        var (rangeStart, rangeEnd) = SeriesRange(start, end);
        string resolvedBucket = ResolveBucket(rangeStart, rangeEnd, bucket);
        if (BucketCount(rangeStart, rangeEnd, resolvedBucket) > MaxSeriesBuckets)
        {
            throw new ArgumentOutOfRangeException(nameof(bucket), "The range has too many buckets; choose a coarser bucket.");
        }

        var roleSet = new HashSet<string>(roles is { Count: > 0 } ? roles : AiUsageRoles.NamedGroups, StringComparer.OrdinalIgnoreCase);
        string split = (splitBy?.Trim()) switch
        {
            "role" => "role", "provider" => "provider", "model" => "model", "keySource" => "keySource", "user" => "user", _ => "none"
        };
        if (split == "user" && (userId != null || !IncludesUser(keys))) split = "none";

        var buckets = BucketStarts(rangeStart, rangeEnd, resolvedBucket);
        var bucketIndex = buckets.Select((b, i) => (b, i)).ToDictionary(x => x.b.Date, x => x.i);
        int IndexOf(DateTime t) => bucketIndex.TryGetValue(BucketStart(t, resolvedBucket), out int i) ? i : -1;

        var usage = new List<SeriesUsage>();
        DateTime? costSince = null;
        if (IncludesSystem(keys))
        {
            var q = InRange(_db.SystemAiUsageLogs.AsNoTracking(), rangeStart, rangeEnd);
            if (userId != null) q = q.Where(l => l.AspNetUserId == userId);
            usage.AddRange((await q
                .GroupBy(l => new { Day = l.TimestampUtc.Date, l.Provider, l.ModelId, l.RoleContext })
                .Select(g => new
                {
                    g.Key.Day, g.Key.Provider, g.Key.ModelId, g.Key.RoleContext,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    DurationSum = g.Sum(x => (long)(x.TotalDurationMs ?? 0)),
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null)
                })
                .ToListAsync(ct))
                .Select(a => new SeriesUsage(a.Day, "system", null, a.Provider, a.ModelId, a.RoleContext, a.Requests, a.Input, a.Output,
                    a.CacheRead, a.Cost, a.Priced, a.DurationSum, a.DurationCount)));
            costSince = Earliest(costSince, await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct));
        }
        if (IncludesUser(keys))
        {
            var q = InRange(_db.UserKeyAiUsageLogs.AsNoTracking(), rangeStart, rangeEnd);
            if (userId != null) q = q.Where(l => l.AspNetUserId == userId);
            usage.AddRange((await q
                .GroupBy(l => new { Day = l.TimestampUtc.Date, l.AspNetUserId, l.Provider, l.ModelId, l.RoleContext })
                .Select(g => new
                {
                    g.Key.Day, g.Key.AspNetUserId, g.Key.Provider, g.Key.ModelId, g.Key.RoleContext,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    DurationSum = g.Sum(x => (long)(x.TotalDurationMs ?? 0)),
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null)
                })
                .ToListAsync(ct))
                .Select(a => new SeriesUsage(a.Day, "user", a.AspNetUserId, a.Provider, a.ModelId, a.RoleContext, a.Requests, a.Input,
                    a.Output, a.CacheRead, a.Cost, a.Priced, a.DurationSum, a.DurationCount)));
            costSince = Earliest(costSince, await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct));
        }
        usage = usage.Where(u => roleSet.Contains(AiUsageRoles.GroupOf(u.RoleContext))).ToList();

        // System ledger rows carry no user, so under a user filter only the user-key ones remain.
        var attempts = (await LoadAttemptsAsync(rangeStart, rangeEnd, keys, userId, ct))
            .Where(a => roleSet.Contains(AiUsageRoles.GroupOf(a.RoleContext)))
            .ToList();

        var modelNames = new Dictionary<(string, string), string?>();
        string KeyOf(string keySourceValue, string? rowUserId, string provider, string modelId, int roleContext) => split switch
        {
            "role" => AiUsageRoles.GroupOf(roleContext),
            "provider" => provider,
            "model" => provider + "|" + modelId,
            "keySource" => keySourceValue,
            "user" => keySourceValue == "system" ? "system" : "user:" + rowUserId,
            _ => "all"
        };

        var lines = new Dictionary<string, AiTelemetrySeriesLineDto>(StringComparer.Ordinal);
        AiTelemetrySeriesLineDto LineFor(string key, string provider, string modelId)
        {
            if (!lines.TryGetValue(key, out var line))
            {
                line = new AiTelemetrySeriesLineDto
                {
                    Key = key,
                    Provider = split switch { "provider" or "model" => provider, _ => null },
                    Points = buckets.Select(_ => new AiTelemetryPointDto()).ToList()
                };
                line.Label = split switch
                {
                    "model" => ModelName(provider, modelId, modelNames) ?? modelId,
                    _ => key
                };
                lines[key] = line;
            }
            return line;
        }

        foreach (var u in usage)
        {
            int i = IndexOf(u.Day);
            if (i < 0) continue;
            var p = LineFor(KeyOf(u.KeySource, u.UserId, u.Provider, u.ModelId, u.RoleContext), u.Provider, u.ModelId).Points[i];
            p.Requests += u.Requests;
            p.PromptTokens += AiUsageRoles.PromptTokens(u.RoleContext, u.Input, u.CacheRead);
            p.CacheReadTokens += u.CacheRead;
            p.OutputTokens += u.Output;
            p.EstimatedCost += u.Cost;
            p.PricedRequests += u.Priced;
            p.DurationSumMs += u.DurationSum;
            p.DurationCount += u.DurationCount;
        }

        foreach (var a in attempts)
        {
            int i = IndexOf(a.StartedAtUtc);
            if (i < 0) continue;
            string keySourceValue = a.KeySource == AiKeySource.UserKey ? "user" : "system";
            var p = LineFor(KeyOf(keySourceValue, a.UserId, a.Provider, a.ModelId, a.RoleContext), a.Provider, a.ModelId).Points[i];
            if (ProviderAttemptOutcomes.IsValid(a.Outcome)) p.ValidAttempts++;
            if (ProviderAttemptOutcomes.IsProvider(a.Outcome)) p.ProviderErrors++;
        }

        await LabelLinesAsync(lines.Values, split, ct);

        var ordered = lines.Values.OrderByDescending(l => l.Points.Sum(p => p.Requests)).ThenBy(l => l.Label, StringComparer.Ordinal).ToList();
        var series = ordered.Take(MaxSeriesLines).ToList();
        var rest = ordered.Skip(MaxSeriesLines).ToList();
        if (rest.Count > 0)
        {
            var other = new AiTelemetrySeriesLineDto
            {
                Key = OtherLineKey,
                Label = "Other",
                IsOther = true,
                Points = buckets.Select((_, i) => Sum(rest.Select(l => l.Points[i]))).ToList()
            };
            series.Add(other);
        }

        string? userName = userId == null ? null
            : await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);

        var (activity, countedSince) = await BuildActivitySeriesAsync(buckets, resolvedBucket, rangeStart, rangeEnd, userId, ct);

        return new AiTelemetrySeriesDto
        {
            Bucket = resolvedBucket,
            SplitBy = split,
            BucketStartsUtc = buckets,
            Series = series,
            CostRecordedSinceUtc = Utc(costSince),
            AttemptsRecordedSinceUtc = Utc(await _db.ChatProviderAttempts.MinAsync(a => (DateTime?)a.StartedAtUtc, ct)),
            UserId = userId,
            UserName = userName,
            Activity = activity,
            ActivityCountedSinceUtc = countedSince
        };
    }

    private async Task LabelLinesAsync(IEnumerable<AiTelemetrySeriesLineDto> lines, string split, CancellationToken ct)
    {
        var list = lines.ToList();
        switch (split)
        {
            case "none":
                foreach (var l in list) l.Label = "All";
                break;
            case "role":
                foreach (var l in list)
                {
                    l.Label = l.Key switch
                    {
                        AiUsageRoles.Chat => "Chat",
                        AiUsageRoles.TitleGeneration => "Title Generation",
                        AiUsageRoles.Benchmark => "Benchmark",
                        _ => "Other roles"
                    };
                }
                break;
            case "keySource":
                foreach (var l in list) l.Label = l.Key == "user" ? "User keys" : "System AI configurations";
                break;
            case "user":
                var ids = list.Where(l => l.Key.StartsWith("user:", StringComparison.Ordinal)).Select(l => l.Key[5..]).ToList();
                var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
                foreach (var l in list)
                {
                    l.Label = l.Key == "system" ? "System AI configurations"
                        : names.TryGetValue(l.Key[5..], out var name) && !string.IsNullOrEmpty(name) ? name! : "Unknown user";
                }
                break;
        }
    }

    private static AiTelemetryPointDto Sum(IEnumerable<AiTelemetryPointDto> points)
    {
        var sum = new AiTelemetryPointDto();
        foreach (var p in points)
        {
            sum.Requests += p.Requests;
            sum.PromptTokens += p.PromptTokens;
            sum.CacheReadTokens += p.CacheReadTokens;
            sum.OutputTokens += p.OutputTokens;
            sum.EstimatedCost += p.EstimatedCost;
            sum.PricedRequests += p.PricedRequests;
            sum.DurationSumMs += p.DurationSumMs;
            sum.DurationCount += p.DurationCount;
            sum.ValidAttempts += p.ValidAttempts;
            sum.ProviderErrors += p.ProviderErrors;
        }
        return sum;
    }

    /// <summary>Activity per bucket; under a user filter only that user's rows (no anonymous row).</summary>
    private async Task<(List<AiTelemetryActivityPointDto> Points, DateOnly? CountedSince)> BuildActivitySeriesAsync(
        List<DateTime> buckets, string bucket, DateTime rangeStart, DateTime rangeEnd, string? userId, CancellationToken ct)
    {
        var q = InRange(_db.OverseerDailyActivities.AsNoTracking(), rangeStart, rangeEnd);
        if (userId != null) q = q.Where(a => a.AspNetUserId == userId);
        var rows = await q.ToListAsync(ct);

        var points = buckets.Select(_ => new AiTelemetryActivityPointDto()).ToList();
        var index = buckets.Select((b, i) => (b, i)).ToDictionary(x => x.b.Date, x => x.i);
        var activeByBucket = buckets.Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToList();

        foreach (var r in rows)
        {
            if (!index.TryGetValue(BucketStart(r.DateUtc.ToDateTime(TimeOnly.MinValue), bucket), out int i)) continue;
            var p = points[i];
            p.ChatsCreated += r.ChatsCreated;
            p.ConfidentialChatsCreated += r.ConfidentialChatsCreated;
            p.GnollHackChatsCreated += r.GnollHackChatsCreated;
            p.IncognitoChatsCreated += r.IncognitoChatsCreated;
            p.UserMessages += r.UserMessages;
            p.AssistantMessages += r.AssistantMessages;
            p.IncognitoUserMessages += r.IncognitoUserMessages;
            if (r.AspNetUserId != null && r.ChatsCreated + r.UserMessages > 0) activeByBucket[i].Add(r.AspNetUserId);
        }

        var allActive = activeByBucket.SelectMany(s => s).Distinct().ToList();
        var firstDays = allActive.Count > 0 ? await FirstActivityDaysAsync(allActive, ct) : new Dictionary<string, DateOnly>();
        for (int i = 0; i < points.Count; i++)
        {
            points[i].ActiveUsers = activeByBucket[i].Count;
            points[i].NewUsers = activeByBucket[i].Count(id => firstDays.TryGetValue(id, out var first)
                && index.TryGetValue(BucketStart(first.ToDateTime(TimeOnly.MinValue), bucket), out int firstBucket) && firstBucket == i);
        }

        var countedSince = await _db.OverseerDailyActivities.MinAsync(a => (DateOnly?)a.DateUtc, ct);
        return (points, countedSince);
    }

    // --- Users -----------------------------------------------------------------------------------

    /// <param name="sort">cost | requests | tokens | peakHour | refusals | lastActive, descending; ties by user name.</param>
    public async Task<AiTelemetryUsersPageDto> GetUsersAsync(DateTime? start, DateTime? end, string? search, string? userId,
        string? sort, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        skip = Math.Max(0, skip);
        var usage = InRange(_db.SystemAiUsageLogs.AsNoTracking(), start, end);
        if (!string.IsNullOrWhiteSpace(userId)) usage = usage.Where(l => l.AspNetUserId == userId);

        var byRole = await usage
            .GroupBy(l => new { l.AspNetUserId, l.RoleContext })
            .Select(g => new
            {
                g.Key.AspNetUserId, g.Key.RoleContext,
                Requests = g.LongCount(),
                Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                Priced = g.LongCount(x => x.EstimatedCost != null),
                Last = g.Max(x => x.TimestampUtc)
            })
            .ToListAsync(ct);

        var userIds = byRole.Select(r => r.AspNetUserId).Distinct().ToList();
        var users = await _db.Users.OfType<ApplicationUser>().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.Email, Blocked = u.OverseerBlockedUtc != null })
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            users = users.Where(u => (u.UserName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (u.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        var selectedIds = users.Select(u => u.Id).ToList();
        if (selectedIds.Count == 0)
        {
            return new AiTelemetryUsersPageDto { SecurityEventsAvailable = true };
        }

        var byConfig = await usage
            .Where(l => selectedIds.Contains(l.AspNetUserId))
            .GroupBy(l => new { l.AspNetUserId, l.SystemAiApiConfigurationId })
            .Select(g => new
            {
                g.Key.AspNetUserId, g.Key.SystemAiApiConfigurationId,
                Requests = g.LongCount(),
                Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                DisplayName = g.Max(x => x.ModelDisplayName)
            })
            .ToListAsync(ct);

        var byHour = await usage
            .Where(l => selectedIds.Contains(l.AspNetUserId))
            .GroupBy(l => new { l.AspNetUserId, Day = l.TimestampUtc.Date, l.TimestampUtc.Hour })
            .Select(g => new { g.Key.AspNetUserId, g.Key.Day, g.Key.Hour, Count = g.Count() })
            .ToListAsync(ct);

        var activity = await InRange(_db.OverseerDailyActivities.AsNoTracking(), start, end)
            .Where(a => a.AspNetUserId != null && selectedIds.Contains(a.AspNetUserId))
            .GroupBy(a => a.AspNetUserId!)
            .Select(g => new { UserId = g.Key, Chats = g.Sum(a => a.ChatsCreated), Messages = g.Sum(a => a.UserMessages) })
            .ToDictionaryAsync(x => x.UserId, ct);

        var events = _db.SecurityEvents.AsNoTracking().Where(e => e.AspNetUserId != null && selectedIds.Contains(e.AspNetUserId));
        if (start.HasValue) { var from = start.Value; events = events.Where(e => e.LastOccurredUtc >= from); }
        if (end.HasValue) { var to = end.Value.AddDays(1); events = events.Where(e => e.OccurredUtc < to); }
        var eventCounts = await events
            .GroupBy(e => new { e.AspNetUserId, e.Kind })
            .Select(g => new { g.Key.AspNetUserId, g.Key.Kind, Count = g.Sum(e => e.Count) })
            .ToListAsync(ct);

        var heldIds = await GnollHackServer.Data.Security.AccountHoldQueries.HeldUserIds(_db)
            .Where(id => selectedIds.Contains(id)).Distinct().ToListAsync(ct);

        var configIds = byConfig.Select(c => c.SystemAiApiConfigurationId).Distinct().ToList();
        var configs = await _db.SystemAiApiConfigurations.AsNoTracking()
            .Where(c => configIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
        var assignments = await _db.UserSystemAiApiConfigurations.AsNoTracking()
            .Where(a => selectedIds.Contains(a.AspNetUserId) && configIds.Contains(a.SystemAiApiConfigurationId))
            .ToListAsync(ct);
        var userGroups = await _db.UserGroups.AsNoTracking()
            .Where(ug => selectedIds.Contains(ug.AspNetUserId))
            .Select(ug => new { ug.AspNetUserId, ug.GroupId })
            .ToListAsync(ct);
        var groupIds = userGroups.Select(g => g.GroupId).Distinct().ToList();
        var groupAssignments = await _db.GroupSystemAiApiConfigurations.AsNoTracking()
            .Where(g => groupIds.Contains(g.GroupId) && configIds.Contains(g.SystemAiApiConfigurationId) && g.IsEnabled)
            .Select(g => new { g.GroupId, g.SystemAiApiConfigurationId })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var rows = new List<AiTelemetryUserRowDto>();
        foreach (var u in users)
        {
            var roles = byRole.Where(r => r.AspNetUserId == u.Id).ToList();
            var hours = byHour.Where(h => h.AspNetUserId == u.Id).ToList();
            var peak = hours.OrderByDescending(h => h.Count).ThenBy(h => h.Day).ThenBy(h => h.Hour).FirstOrDefault();
            var counts = eventCounts.Where(e => e.AspNetUserId == u.Id).ToList();
            activity.TryGetValue(u.Id, out var act);
            var groupsOfUser = userGroups.Where(g => g.AspNetUserId == u.Id).Select(g => g.GroupId).ToHashSet();

            var row = new AiTelemetryUserRowDto
            {
                UserId = u.Id,
                UserName = u.UserName ?? string.Empty,
                Requests = roles.Sum(r => r.Requests),
                ChatRequests = roles.Where(r => AiUsageRoles.GroupOf(r.RoleContext) == AiUsageRoles.Chat).Sum(r => r.Requests),
                TitleRequests = roles.Where(r => AiUsageRoles.GroupOf(r.RoleContext) == AiUsageRoles.TitleGeneration).Sum(r => r.Requests),
                BenchmarkRequests = roles.Where(r => AiUsageRoles.GroupOf(r.RoleContext) == AiUsageRoles.Benchmark).Sum(r => r.Requests),
                PromptTokens = roles.Sum(r => AiUsageRoles.PromptTokens(r.RoleContext, r.Input, r.CacheRead)),
                OutputTokens = roles.Sum(r => r.Output),
                EstimatedCost = roles.Sum(r => r.Cost),
                PricedRequests = roles.Sum(r => r.Priced),
                PeakHourRequests = peak?.Count ?? 0,
                PeakHourStartUtc = peak == null ? null : DateTime.SpecifyKind(peak.Day.AddHours(peak.Hour), DateTimeKind.Utc),
                RequestsPerActiveHour = hours.Count > 0 ? Math.Round((double)hours.Sum(h => h.Count) / hours.Count, 2) : 0,
                ActiveDays = hours.Select(h => h.Day).Distinct().Count(),
                LastRequestUtc = roles.Count > 0 ? DateTime.SpecifyKind(roles.Max(r => r.Last), DateTimeKind.Utc) : null,
                ChatsCreated = act?.Chats ?? 0,
                UserMessages = act?.Messages ?? 0,
                LimitRefusals = counts.Where(c => c.Kind is SecurityEventKind.QuotaRefused or SecurityEventKind.RateLimitRejected).Sum(c => c.Count),
                ProviderRefusals = counts.Where(c => c.Kind is SecurityEventKind.ProviderRefusal or SecurityEventKind.ProviderSafetyBlock
                    or SecurityEventKind.ProviderPolicyRejection).Sum(c => c.Count),
                IsOverseerBlocked = u.Blocked,
                HasAccountHold = heldIds.Contains(u.Id)
            };

            foreach (var c in byConfig.Where(c => c.AspNetUserId == u.Id).OrderByDescending(c => c.Requests))
            {
                configs.TryGetValue(c.SystemAiApiConfigurationId, out var config);
                var assignment = assignments.FirstOrDefault(a => a.AspNetUserId == u.Id && a.SystemAiApiConfigurationId == c.SystemAiApiConfigurationId);
                bool groupGrants = groupAssignments.Any(g => g.SystemAiApiConfigurationId == c.SystemAiApiConfigurationId && groupsOfUser.Contains(g.GroupId));

                row.Configs.Add(new AiTelemetryUserConfigDto
                {
                    ConfigId = c.SystemAiApiConfigurationId,
                    ConfigName = !string.IsNullOrEmpty(config?.DisplayName) ? config!.DisplayName : c.DisplayName ?? config?.ModelId ?? c.SystemAiApiConfigurationId.ToString(CultureInfo.InvariantCulture),
                    Requests = c.Requests,
                    EstimatedCost = c.Cost,
                    AssignmentId = assignment?.Id,
                    AssignmentEnabled = assignment?.IsEnabled,
                    DailyChatRequests = assignment == null ? null : EffectiveDaily(assignment, now, assignment.DailyChatRequestsCount),
                    MaxDailyChatRequests = assignment?.MaxDailyChatRequests,
                    MonthlyChatRequests = assignment == null ? null : EffectiveMonthly(assignment, now, assignment.MonthlyChatRequestsCount),
                    MaxMonthlyChatRequests = assignment?.MaxMonthlyChatRequests,
                    DailyChatTokens = assignment == null ? null : (assignment.LastDailyReset == null || assignment.LastDailyReset.Value.Date < now.Date ? 0 : assignment.DailyChatTokensCount),
                    MaxDailyChatTokens = assignment?.MaxDailyChatTokens,
                    LimitSource = assignment is { IsEnabled: true } ? "user"
                        : groupGrants ? "group"
                        : config?.IsSystemWide == true ? "systemWide"
                        : "none"
                });
            }
            rows.Add(row);
        }

        IOrderedEnumerable<AiTelemetryUserRowDto> ordered = (sort?.Trim()) switch
        {
            "requests" => rows.OrderByDescending(r => r.Requests),
            "tokens" => rows.OrderByDescending(r => r.PromptTokens + r.OutputTokens),
            "peakHour" => rows.OrderByDescending(r => r.PeakHourRequests),
            "refusals" => rows.OrderByDescending(r => r.LimitRefusals + r.ProviderRefusals),
            "lastActive" => rows.OrderByDescending(r => r.LastRequestUtc ?? DateTime.MinValue),
            _ => rows.OrderByDescending(r => r.EstimatedCost)
        };

        return new AiTelemetryUsersPageDto
        {
            TotalUsers = rows.Count,
            SecurityEventsAvailable = true,
            Users = ordered.ThenBy(r => r.UserName, StringComparer.OrdinalIgnoreCase).Skip(skip).Take(take).ToList()
        };
    }

    /// <summary>A daily counter as the next request would see it: 0 when its last reset was before today (UTC).</summary>
    private static int EffectiveDaily(IRateLimitedEntity entity, DateTime now, int count) =>
        entity.LastDailyReset == null || entity.LastDailyReset.Value.Date < now.Date ? 0 : count;

    /// <summary>A monthly counter as the next request would see it: 0 when its last reset was in an earlier month.</summary>
    private static int EffectiveMonthly(IRateLimitedEntity entity, DateTime now, int count) =>
        entity.LastMonthlyReset == null
        || entity.LastMonthlyReset.Value.Year < now.Year
        || (entity.LastMonthlyReset.Value.Year == now.Year && entity.LastMonthlyReset.Value.Month < now.Month) ? 0 : count;

    private static DateTime? Earliest(DateTime? a, DateTime? b) =>
        a == null ? b : b == null ? a : (a < b ? a : b);

    private static DateTime? Utc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
