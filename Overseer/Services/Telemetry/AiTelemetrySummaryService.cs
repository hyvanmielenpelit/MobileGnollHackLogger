namespace Overseer.Services.Telemetry;

using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;

/// <summary>A model as the charts' model filter names it, written <c>provider|modelId</c>.</summary>
public readonly record struct AiTelemetryModelRef(string Provider, string ModelId)
{
    /// <summary>
    /// Splits at the first <c>|</c>, so a model id may contain <c>:</c> (Ollama's <c>llama3:8b</c>) or <c>|</c>; both parts
    /// must be non-blank.
    /// </summary>
    public static bool TryParse(string? value, out AiTelemetryModelRef result)
    {
        result = default;
        if (string.IsNullOrEmpty(value)) return false;
        int bar = value.IndexOf('|');
        if (bar < 0) return false;
        string provider = value[..bar].Trim();
        string modelId = value[(bar + 1)..].Trim();
        if (provider.Length == 0 || modelId.Length == 0) return false;
        result = new AiTelemetryModelRef(provider, modelId);
        return true;
    }

    public override string ToString() => Provider + "|" + ModelId;
}

/// <summary>
/// The AI Telemetry tab's queries: the summary and breakdown, the series over time and its filter options, per-user and
/// per-group usage of the selected key sources, and the usage of one model by user and of one user or group by model.
/// Reads <see cref="SystemAiUsageLog"/>, <see cref="UserKeyAiUsageLog"/>, <see cref="ChatProviderAttempt"/>,
/// <see cref="OverseerDailyActivity"/>, group membership and, for refusal counts, <see cref="SecurityEvent"/>. Token
/// figures are normalized per role context (<see cref="AiUsageRoles"/>). A group's usage is the usage of its current
/// members over the whole range; usage logs record no group.
/// </summary>
public class AiTelemetrySummaryService
{
    public const int MaxSeriesBuckets = 1100;
    public const int MaxSeriesLines = 8;
    public const string OtherLineKey = "other";
    public const int MaxSeriesFilterUsers = 50;
    public const int MaxSeriesFilterModels = 50;
    public const int MaxSeriesFilterGroups = 50;
    public const int MaxSeriesOptionUsers = 1000;

    /// <summary>The wire value of "No group": the users who belong to no group.</summary>
    public const string NoGroupKey = "none";
    public const string NoGroupName = "No group";
    public const string UnknownGroupName = "Unknown group";
    public const string OwnKeysLineName = "Members' own keys";

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

    /// <summary>The user and group filters; an empty list filters nothing, and a null group id is "No group".</summary>
    private sealed record Audience(IReadOnlyList<string> UserIds, IReadOnlyList<long?> GroupIds)
    {
        public static readonly Audience All = new(Array.Empty<string>(), Array.Empty<long?>());

        public static Audience OfUser(string userId) => new(new[] { userId }, Array.Empty<long?>());

        public static Audience OfGroup(long? groupId) => new(Array.Empty<string>(), new[] { groupId });
    }

    /// <summary>
    /// Narrows a query to the audience by the user id <paramref name="userIdOf"/> selects: the chosen users, AND the
    /// current members of the chosen groups (a subquery on <c>UserGroups</c>, OR-ed with "no membership at all" for
    /// "No group"). Rows without a user never match a filter.
    /// </summary>
    private IQueryable<T> ApplyAudience<T>(IQueryable<T> q, Expression<Func<T, string?>> userIdOf, Audience audience)
    {
        if (audience.UserIds.Count == 1)
        {
            string only = audience.UserIds[0];
            q = q.Where(Compose(userIdOf, id => id == only));
        }
        else if (audience.UserIds.Count > 1)
        {
            var ids = audience.UserIds.ToList();
            q = q.Where(Compose(userIdOf, id => id != null && ids.Contains(id)));
        }

        if (audience.GroupIds.Count > 0)
        {
            var groupIds = audience.GroupIds.Where(g => g.HasValue).Select(g => g!.Value).ToList();
            bool noGroup = audience.GroupIds.Any(g => g == null);
            IQueryable<UserGroup> userGroups = _db.UserGroups;
            IQueryable<string> members = userGroups.Where(ug => groupIds.Contains(ug.GroupId)).Select(ug => ug.AspNetUserId);

            Expression<Func<string?, bool>> inGroups;
            if (groupIds.Count == 0)
            {
                inGroups = id => id != null && !userGroups.Any(ug => ug.AspNetUserId == id);
            }
            else if (noGroup)
            {
                inGroups = id => id != null && (members.Contains(id) || !userGroups.Any(ug => ug.AspNetUserId == id));
            }
            else
            {
                inGroups = id => id != null && members.Contains(id);
            }
            q = q.Where(Compose(userIdOf, inGroups));
        }
        return q;
    }

    /// <summary><c>x =&gt; predicate(userIdOf(x))</c>, inlined so that it translates.</summary>
    private static Expression<Func<T, bool>> Compose<T>(Expression<Func<T, string?>> userIdOf, Expression<Func<string?, bool>> predicate) =>
        Expression.Lambda<Func<T, bool>>(new ParameterReplacer(predicate.Parameters[0], userIdOf.Body).Visit(predicate.Body), userIdOf.Parameters);

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>Accounts that belong to no group.</summary>
    private Task<int> CountUngroupedAccountsAsync(CancellationToken ct)
    {
        IQueryable<UserGroup> userGroups = _db.UserGroups;
        return _db.Users.CountAsync(u => !userGroups.Any(ug => ug.AspNetUserId == u.Id), ct);
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

        var attempts = await LoadAttemptsAsync(start, end, keys, Audience.All, modelIds: null, ct);
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

    /// <param name="modelIds">A prefilter on the requested model id; the caller matches the provider too.</param>
    private async Task<List<AttemptRow>> LoadAttemptsAsync(DateTime? start, DateTime? end, string keys, Audience audience,
        List<string>? modelIds, CancellationToken ct)
    {
        var q = InRange(_db.ChatProviderAttempts.AsNoTracking(), start, end);
        if (!IncludesSystem(keys)) q = q.Where(a => a.KeySource == AiKeySource.UserKey);
        if (!IncludesUser(keys)) q = q.Where(a => a.KeySource == AiKeySource.System);
        q = ApplyAudience(q, a => a.AspNetUserId, audience);
        if (modelIds != null) q = q.Where(a => modelIds.Contains(a.RequestedModelId));

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

    /// <summary>
    /// The series. A category with nothing chosen is not filtered; choices within a category combine with OR, the
    /// categories with AND. The activity figures take the user and group filters, never the model filter.
    /// </summary>
    /// <param name="roles">Role groups to include; null or empty: the three named groups.</param>
    /// <param name="splitBy">none | role | provider | model | keySource | user; user falls back to none without user keys or with exactly one user chosen.</param>
    /// <param name="userIds">Users to include.</param>
    /// <param name="models">Models to include, matched on provider and model id.</param>
    /// <param name="groupIds">Groups whose current members to include; null is "No group".</param>
    public async Task<AiTelemetrySeriesDto> GetSeriesAsync(DateTime? start, DateTime? end, string? keySource, IReadOnlyCollection<string>? roles,
        string? bucket, string? splitBy, IReadOnlyList<string>? userIds, IReadOnlyList<AiTelemetryModelRef>? models,
        IReadOnlyList<long?>? groupIds, CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        var audience = new Audience(
            userIds?.Distinct(StringComparer.Ordinal).ToList() ?? new List<string>(),
            groupIds?.Distinct().ToList() ?? new List<long?>());
        var modelList = models?.Distinct().ToList() ?? new List<AiTelemetryModelRef>();
        var modelSet = modelList.Count > 0 ? modelList.ToHashSet() : null;
        var modelIds = modelSet?.Select(m => m.ModelId).Distinct(StringComparer.Ordinal).ToList();
        bool ModelChosen(string provider, string modelId) => modelSet == null || modelSet.Contains(new AiTelemetryModelRef(provider, modelId));

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
        if (split == "user" && (audience.UserIds.Count == 1 || !IncludesUser(keys))) split = "none";

        var buckets = BucketStarts(rangeStart, rangeEnd, resolvedBucket);
        var bucketIndex = buckets.Select((b, i) => (b, i)).ToDictionary(x => x.b.Date, x => x.i);
        int IndexOf(DateTime t) => bucketIndex.TryGetValue(BucketStart(t, resolvedBucket), out int i) ? i : -1;

        var usage = new List<SeriesUsage>();
        DateTime? costSince = null;
        if (IncludesSystem(keys))
        {
            var q = ApplyAudience(InRange(_db.SystemAiUsageLogs.AsNoTracking(), rangeStart, rangeEnd), l => l.AspNetUserId, audience);
            if (modelIds != null) q = q.Where(l => modelIds.Contains(l.ModelId));
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
            var q = ApplyAudience(InRange(_db.UserKeyAiUsageLogs.AsNoTracking(), rangeStart, rangeEnd), l => l.AspNetUserId, audience);
            if (modelIds != null) q = q.Where(l => modelIds.Contains(l.ModelId));
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
        usage = usage.Where(u => roleSet.Contains(AiUsageRoles.GroupOf(u.RoleContext)) && ModelChosen(u.Provider, u.ModelId)).ToList();

        // System ledger rows carry no user, so under a user or group filter only the user-key ones remain.
        var attempts = (await LoadAttemptsAsync(rangeStart, rangeEnd, keys, audience, modelIds, ct))
            .Where(a => roleSet.Contains(AiUsageRoles.GroupOf(a.RoleContext)) && ModelChosen(a.Provider, a.ModelId))
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

        var (activity, countedSince) = await BuildActivitySeriesAsync(buckets, resolvedBucket, rangeStart, rangeEnd, audience, ct);

        return new AiTelemetrySeriesDto
        {
            Bucket = resolvedBucket,
            SplitBy = split,
            BucketStartsUtc = buckets,
            Series = series,
            CostRecordedSinceUtc = Utc(costSince),
            AttemptsRecordedSinceUtc = Utc(await _db.ChatProviderAttempts.MinAsync(a => (DateTime?)a.StartedAtUtc, ct)),
            Users = await UserRefsAsync(audience.UserIds, ct),
            Models = modelList
                .Select(m => new AiTelemetrySeriesModelRefDto { Provider = m.Provider, ModelId = m.ModelId, ModelName = ModelName(m.Provider, m.ModelId, modelNames) })
                .ToList(),
            Groups = await GroupRefsAsync(audience.GroupIds, ct),
            Activity = activity,
            ActivityCountedSinceUtc = countedSince
        };
    }

    /// <summary>The user filter with current names, in request order; null when the account is gone.</summary>
    private async Task<List<AiTelemetrySeriesUserRefDto>> UserRefsAsync(IReadOnlyList<string> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new();
        var ids = userIds.ToList();
        var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
        return userIds.Select(id => new AiTelemetrySeriesUserRefDto { UserId = id, UserName = names.TryGetValue(id, out var name) ? name : null }).ToList();
    }

    /// <summary>The group filter with current names, in request order: "No group" for null, "Unknown group" for an id no group has.</summary>
    private async Task<List<AiTelemetrySeriesGroupRefDto>> GroupRefsAsync(IReadOnlyList<long?> groupIds, CancellationToken ct)
    {
        if (groupIds.Count == 0) return new();
        var ids = groupIds.Where(g => g.HasValue).Select(g => g!.Value).ToList();
        var names = ids.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.DisplayName, ct);
        return groupIds.Select(id => new AiTelemetrySeriesGroupRefDto
        {
            GroupId = id,
            GroupName = id is long known ? (names.TryGetValue(known, out var name) ? name : UnknownGroupName) : NoGroupName
        }).ToList();
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

    /// <summary>Activity per bucket; under a user or group filter only the audience's rows (no anonymous row).</summary>
    private async Task<(List<AiTelemetryActivityPointDto> Points, DateOnly? CountedSince)> BuildActivitySeriesAsync(
        List<DateTime> buckets, string bucket, DateTime rangeStart, DateTime rangeEnd, Audience audience, CancellationToken ct)
    {
        var q = ApplyAudience(InRange(_db.OverseerDailyActivities.AsNoTracking(), rangeStart, rangeEnd), a => a.AspNetUserId, audience);
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

    // --- Series options --------------------------------------------------------------------------

    /// <summary>One user's sums of one model in one key source and role context, as the series options fold them.</summary>
    private sealed record OptionUsage(string KeySource, string UserId, string Provider, string ModelId, int RoleContext, long Requests,
        long Input, long CacheRead, long Output, decimal Cost, long Priced, DateTime Last);

    /// <summary>
    /// The models, users and groups the charts offer for the series' range (<see cref="SeriesRange"/>), key sources and
    /// roles. Models include those seen only in the attempts ledger, with 0 requests; users are the
    /// <see cref="MaxSeriesOptionUsers"/> with the most requests; groups are every group, plus "No group" when an
    /// ungrouped user has usage in the scope.
    /// </summary>
    /// <param name="roles">Role groups to include; null or empty: the three named groups.</param>
    public async Task<AiTelemetrySeriesOptionsDto> GetSeriesOptionsAsync(DateTime? start, DateTime? end, string? keySource,
        IReadOnlyCollection<string>? roles, CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        var (rangeStart, rangeEnd) = SeriesRange(start, end);
        var roleSet = new HashSet<string>(roles is { Count: > 0 } ? roles : AiUsageRoles.NamedGroups, StringComparer.OrdinalIgnoreCase);

        var usage = new List<OptionUsage>();
        if (IncludesSystem(keys))
        {
            usage.AddRange((await InRange(_db.SystemAiUsageLogs.AsNoTracking(), rangeStart, rangeEnd)
                .GroupBy(l => new { l.AspNetUserId, l.Provider, l.ModelId, l.RoleContext })
                .Select(g => new
                {
                    g.Key.AspNetUserId, g.Key.Provider, g.Key.ModelId, g.Key.RoleContext,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    Last = g.Max(x => x.TimestampUtc)
                })
                .ToListAsync(ct))
                .Select(a => new OptionUsage("system", a.AspNetUserId, a.Provider, a.ModelId, a.RoleContext, a.Requests, a.Input, a.CacheRead,
                    a.Output, a.Cost, a.Priced, a.Last)));
        }
        if (IncludesUser(keys))
        {
            usage.AddRange((await InRange(_db.UserKeyAiUsageLogs.AsNoTracking(), rangeStart, rangeEnd)
                .GroupBy(l => new { l.AspNetUserId, l.Provider, l.ModelId, l.RoleContext })
                .Select(g => new
                {
                    g.Key.AspNetUserId, g.Key.Provider, g.Key.ModelId, g.Key.RoleContext,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    Last = g.Max(x => x.TimestampUtc)
                })
                .ToListAsync(ct))
                .Select(a => new OptionUsage("user", a.AspNetUserId, a.Provider, a.ModelId, a.RoleContext, a.Requests, a.Input, a.CacheRead,
                    a.Output, a.Cost, a.Priced, a.Last)));
        }
        usage = usage.Where(u => roleSet.Contains(AiUsageRoles.GroupOf(u.RoleContext))).ToList();

        var attemptQuery = InRange(_db.ChatProviderAttempts.AsNoTracking(), rangeStart, rangeEnd);
        if (!IncludesSystem(keys)) attemptQuery = attemptQuery.Where(a => a.KeySource == AiKeySource.UserKey);
        if (!IncludesUser(keys)) attemptQuery = attemptQuery.Where(a => a.KeySource == AiKeySource.System);
        var attemptModels = (await attemptQuery
                .Select(a => new { a.Provider, ModelId = a.RequestedModelId, a.KeySource, a.RoleContext })
                .Distinct()
                .ToListAsync(ct))
            .Where(a => roleSet.Contains(AiUsageRoles.GroupOf(a.RoleContext)))
            .ToList();

        var models = ModelOptions(usage, attemptModels.Select(a => (a.Provider, a.ModelId, a.KeySource == AiKeySource.UserKey ? "user" : "system")));

        var userIds = usage.Select(u => u.UserId).Distinct(StringComparer.Ordinal).ToList();
        var names = userIds.Count == 0
            ? new Dictionary<string, string?>()
            : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
        var users = usage
            .GroupBy(u => u.UserId, StringComparer.Ordinal)
            .Select(g => new AiTelemetrySeriesUserOptionDto
            {
                UserId = g.Key,
                UserName = names.TryGetValue(g.Key, out var name) ? name : null,
                Requests = g.Sum(u => u.Requests),
                SystemRequests = g.Where(u => u.KeySource == "system").Sum(u => u.Requests),
                UserKeyRequests = g.Where(u => u.KeySource == "user").Sum(u => u.Requests),
                EstimatedCost = g.Sum(u => u.Cost),
                PricedRequests = g.Sum(u => u.Priced),
                LastRequestUtc = DateTime.SpecifyKind(g.Max(u => u.Last), DateTimeKind.Utc)
            })
            .OrderByDescending(u => u.Requests)
            .ThenBy(u => u.UserName == null)
            .ThenBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.UserId, StringComparer.Ordinal)
            .ToList();

        var groups = await _db.Groups.AsNoTracking().Select(g => new { g.Id, g.DisplayName }).ToListAsync(ct);
        var memberships = await _db.UserGroups.AsNoTracking().Select(ug => new { ug.AspNetUserId, ug.GroupId }).ToListAsync(ct);
        var membersByGroup = memberships.ToLookup(m => m.GroupId, m => m.AspNetUserId);
        var grouped = memberships.Select(m => m.AspNetUserId).ToHashSet(StringComparer.Ordinal);
        var byUser = users.ToDictionary(u => u.UserId, StringComparer.Ordinal);

        AiTelemetrySeriesGroupOptionDto GroupOption(long? groupId, string groupName, int memberCount, List<AiTelemetrySeriesUserOptionDto> used) => new()
        {
            GroupId = groupId,
            GroupName = groupName,
            MemberCount = memberCount,
            Requests = used.Sum(u => u.Requests),
            EstimatedCost = used.Sum(u => u.EstimatedCost),
            PricedRequests = used.Sum(u => u.PricedRequests),
            LastRequestUtc = used.Count > 0 ? used.Max(u => u.LastRequestUtc) : null
        };

        var groupOptions = groups
            .Select(g =>
            {
                var members = membersByGroup[g.Id].Distinct(StringComparer.Ordinal).ToList();
                return GroupOption(g.Id, g.DisplayName, members.Count,
                    members.Where(byUser.ContainsKey).Select(id => byUser[id]).ToList());
            })
            .ToList();

        var ungrouped = users.Where(u => !grouped.Contains(u.UserId)).ToList();
        if (ungrouped.Count > 0)
        {
            groupOptions.Add(GroupOption(null, NoGroupName, await CountUngroupedAccountsAsync(ct), ungrouped));
        }

        return new AiTelemetrySeriesOptionsDto
        {
            Models = models,
            Users = users.Take(MaxSeriesOptionUsers).ToList(),
            TotalUsers = users.Count,
            UsersTruncated = users.Count > MaxSeriesOptionUsers,
            Groups = groupOptions
                .OrderByDescending(g => g.Requests)
                .ThenBy(g => g.GroupId == null)
                .ThenBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.GroupId)
                .ToList()
        };
    }

    private List<AiTelemetrySeriesModelOptionDto> ModelOptions(List<OptionUsage> usage,
        IEnumerable<(string Provider, string ModelId, string KeySource)> attemptModels)
    {
        var modelNames = new Dictionary<(string, string), string?>();
        var options = usage
            .GroupBy(u => (u.Provider, u.ModelId))
            .Select(g => new AiTelemetrySeriesModelOptionDto
            {
                Provider = g.Key.Provider,
                ModelId = g.Key.ModelId,
                ModelName = ModelName(g.Key.Provider, g.Key.ModelId, modelNames),
                KeySources = g.Select(u => u.KeySource).ToList(),
                Requests = g.Sum(u => u.Requests),
                EstimatedCost = g.Sum(u => u.Cost),
                PricedRequests = g.Sum(u => u.Priced),
                Tokens = g.Sum(u => AiUsageRoles.PromptTokens(u.RoleContext, u.Input, u.CacheRead) + u.Output),
                LastRequestUtc = DateTime.SpecifyKind(g.Max(u => u.Last), DateTimeKind.Utc)
            })
            .ToDictionary(o => (o.Provider, o.ModelId));

        // A model whose every call failed has attempts and no usage rows.
        foreach (var a in attemptModels)
        {
            if (!options.TryGetValue((a.Provider, a.ModelId), out var option))
            {
                option = new AiTelemetrySeriesModelOptionDto
                {
                    Provider = a.Provider,
                    ModelId = a.ModelId,
                    ModelName = ModelName(a.Provider, a.ModelId, modelNames)
                };
                options[(a.Provider, a.ModelId)] = option;
            }
            option.KeySources.Add(a.KeySource);
        }

        foreach (var option in options.Values)
        {
            option.KeySources = new[] { "system", "user" }.Where(option.KeySources.Contains).ToList();
        }

        return options.Values
            .OrderByDescending(o => o.Requests)
            .ThenBy(o => o.ModelName ?? o.ModelId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Provider, StringComparer.Ordinal)
            .ThenBy(o => o.ModelId, StringComparer.Ordinal)
            .ToList();
    }

    // --- Users -----------------------------------------------------------------------------------

    /// <summary>One user's sums in one key source and role context.</summary>
    private sealed record UserRoleUsage(string KeySource, string UserId, int RoleContext, long Requests, long Input, long CacheRead,
        long Output, decimal Cost, long Priced, DateTime Last);

    /// <summary>One user's requests in one UTC clock hour.</summary>
    private sealed record UserHourUsage(string UserId, DateTime Day, int Hour, int Count);

    /// <summary>One user's usage of one system configuration, with the usage rows' ModelDisplayName.</summary>
    private sealed record UserConfigUsage(long ConfigId, long Requests, decimal Cost, string? DisplayName);

    /// <summary>One user's usage of one own-key model; a null id when the log attributes none.</summary>
    private sealed record UserKeyModelUsage(long? UserAiModelId, long Requests, decimal Cost, string? DisplayName, string? ModelId);

    /// <summary>An own-key model's current name and the settings its badges show.</summary>
    private sealed record KeyModelBadges(long Id, string? DisplayName, string? Provider, string? ThinkingLevel, string? ReasoningMode,
        string? ServiceTier);

    /// <summary>One user's usage of the selected key sources over a range, with activity, refusals and account state.</summary>
    private sealed class UserAggregate
    {
        public string UserId { get; init; } = string.Empty;
        public string? UserName { get; init; }
        public bool Blocked { get; init; }
        public bool Held { get; init; }
        public List<UserRoleUsage> Roles { get; init; } = new();
        public List<UserHourUsage> Hours { get; init; } = new();
        public List<UserConfigUsage> Configs { get; init; } = new();
        public List<UserKeyModelUsage> KeyModels { get; init; } = new();
        public int Chats { get; init; }
        public int Messages { get; init; }

        /// <summary>Quota and per-minute refusals.</summary>
        public int LimitRefusals { get; init; }

        /// <summary>Provider refusals, safety blocks and policy rejections.</summary>
        public int ProviderRefusals { get; init; }
    }

    /// <summary>
    /// The aggregates of every user with usage of the selected key sources in the range who still has an account,
    /// optionally narrowed to one user and to a user name or email search.
    /// </summary>
    private async Task<List<UserAggregate>> AggregateUsersAsync(DateTime? start, DateTime? end, string keys, string? userId,
        string? search, CancellationToken ct)
    {
        var systemUsage = InRange(_db.SystemAiUsageLogs.AsNoTracking(), start, end);
        var keyUsage = InRange(_db.UserKeyAiUsageLogs.AsNoTracking(), start, end);
        if (userId != null)
        {
            systemUsage = systemUsage.Where(l => l.AspNetUserId == userId);
            keyUsage = keyUsage.Where(l => l.AspNetUserId == userId);
        }

        var byRole = new List<UserRoleUsage>();
        if (IncludesSystem(keys))
        {
            byRole.AddRange((await systemUsage
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
                .ToListAsync(ct))
                .Select(a => new UserRoleUsage("system", a.AspNetUserId, a.RoleContext, a.Requests, a.Input, a.CacheRead, a.Output,
                    a.Cost, a.Priced, a.Last)));
        }
        if (IncludesUser(keys))
        {
            byRole.AddRange((await keyUsage
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
                .ToListAsync(ct))
                .Select(a => new UserRoleUsage("user", a.AspNetUserId, a.RoleContext, a.Requests, a.Input, a.CacheRead, a.Output,
                    a.Cost, a.Priced, a.Last)));
        }

        var userIds = byRole.Select(r => r.UserId).Distinct().ToList();
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
            return new List<UserAggregate>();
        }

        var byConfig = !IncludesSystem(keys)
            ? new()
            : await systemUsage
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

        var byKeyModel = !IncludesUser(keys)
            ? new()
            : await keyUsage
                .Where(l => selectedIds.Contains(l.AspNetUserId))
                .GroupBy(l => new { l.AspNetUserId, l.UserAiModelId })
                .Select(g => new
                {
                    g.Key.AspNetUserId, g.Key.UserAiModelId,
                    Requests = g.LongCount(),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    DisplayName = g.Max(x => x.ModelDisplayName),
                    ModelId = g.Max(x => x.ModelId)
                })
                .ToListAsync(ct);

        var hourRows = new List<UserHourUsage>();
        if (IncludesSystem(keys))
        {
            hourRows.AddRange((await systemUsage
                .Where(l => selectedIds.Contains(l.AspNetUserId))
                .GroupBy(l => new { l.AspNetUserId, Day = l.TimestampUtc.Date, l.TimestampUtc.Hour })
                .Select(g => new { g.Key.AspNetUserId, g.Key.Day, g.Key.Hour, Count = g.Count() })
                .ToListAsync(ct))
                .Select(h => new UserHourUsage(h.AspNetUserId, h.Day, h.Hour, h.Count)));
        }
        if (IncludesUser(keys))
        {
            hourRows.AddRange((await keyUsage
                .Where(l => selectedIds.Contains(l.AspNetUserId))
                .GroupBy(l => new { l.AspNetUserId, Day = l.TimestampUtc.Date, l.TimestampUtc.Hour })
                .Select(g => new { g.Key.AspNetUserId, g.Key.Day, g.Key.Hour, Count = g.Count() })
                .ToListAsync(ct))
                .Select(h => new UserHourUsage(h.AspNetUserId, h.Day, h.Hour, h.Count)));
        }
        var byHour = hourRows
            .GroupBy(h => (h.UserId, h.Day, h.Hour))
            .Select(g => new UserHourUsage(g.Key.UserId, g.Key.Day, g.Key.Hour, g.Sum(h => h.Count)))
            .ToList();

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

        var heldIds = (await GnollHackServer.Data.Security.AccountHoldQueries.HeldUserIds(_db)
            .Where(id => selectedIds.Contains(id)).Distinct().ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var rolesOf = byRole.ToLookup(r => r.UserId, StringComparer.Ordinal);
        var hoursOf = byHour.ToLookup(h => h.UserId, StringComparer.Ordinal);
        var configsOf = byConfig.ToLookup(c => c.AspNetUserId, StringComparer.Ordinal);
        var keyModelsOf = byKeyModel.ToLookup(m => m.AspNetUserId, StringComparer.Ordinal);
        var eventsOf = eventCounts.ToLookup(e => e.AspNetUserId ?? string.Empty, StringComparer.Ordinal);

        return users.Select(u =>
        {
            activity.TryGetValue(u.Id, out var act);
            var counts = eventsOf[u.Id].ToList();
            return new UserAggregate
            {
                UserId = u.Id,
                UserName = u.UserName,
                Blocked = u.Blocked,
                Held = heldIds.Contains(u.Id),
                Roles = rolesOf[u.Id].ToList(),
                Hours = hoursOf[u.Id].ToList(),
                Configs = configsOf[u.Id].Select(c => new UserConfigUsage(c.SystemAiApiConfigurationId, c.Requests, c.Cost, c.DisplayName)).ToList(),
                KeyModels = keyModelsOf[u.Id].Select(m => new UserKeyModelUsage(m.UserAiModelId, m.Requests, m.Cost, m.DisplayName, m.ModelId)).ToList(),
                Chats = act?.Chats ?? 0,
                Messages = act?.Messages ?? 0,
                LimitRefusals = counts.Where(c => c.Kind is SecurityEventKind.QuotaRefused or SecurityEventKind.RateLimitRejected).Sum(c => c.Count),
                ProviderRefusals = counts.Where(c => c.Kind is SecurityEventKind.ProviderRefusal or SecurityEventKind.ProviderSafetyBlock
                    or SecurityEventKind.ProviderPolicyRejection).Sum(c => c.Count)
            };
        }).ToList();
    }

    /// <param name="keySource">system (default) | user | both.</param>
    /// <param name="sort">cost (default) | requests | tokens | peakHour | refusals | lastActive | name.</param>
    /// <param name="direction">asc | desc (default); ties by user name, then user id. No last request sorts last.</param>
    public async Task<AiTelemetryUsersPageDto> GetUsersAsync(DateTime? start, DateTime? end, string? search, string? userId,
        string? sort, int skip, int take, string? keySource = null, string? direction = null, CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        skip = Math.Max(0, skip);

        var aggregates = await AggregateUsersAsync(start, end, keys, string.IsNullOrWhiteSpace(userId) ? null : userId, search, ct);
        if (aggregates.Count == 0)
        {
            return new AiTelemetryUsersPageDto { KeySource = keys, SecurityEventsAvailable = true };
        }
        var selectedIds = aggregates.Select(a => a.UserId).ToList();

        var configIds = aggregates.SelectMany(a => a.Configs).Select(c => c.ConfigId).Distinct().ToList();
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
        var keyModelIds = aggregates.SelectMany(a => a.KeyModels).Where(m => m.UserAiModelId != null).Select(m => m.UserAiModelId!.Value)
            .Distinct().ToList();
        var keyModels = keyModelIds.Count == 0
            ? new Dictionary<long, KeyModelBadges>()
            : await _db.UserAiModels.AsNoTracking()
                .Where(m => keyModelIds.Contains(m.Id))
                .Select(m => new KeyModelBadges(m.Id, m.DisplayName, m.Provider, m.ThinkingLevel, m.ReasoningMode, m.ServiceTier))
                .ToDictionaryAsync(m => m.Id, ct);

        var now = DateTime.UtcNow;
        var rows = new List<AiTelemetryUserRowDto>();
        foreach (var u in aggregates)
        {
            var roles = u.Roles;
            var hours = u.Hours;
            var peak = hours.OrderByDescending(h => h.Count).ThenBy(h => h.Day).ThenBy(h => h.Hour).FirstOrDefault();
            var groupsOfUser = userGroups.Where(g => g.AspNetUserId == u.UserId).Select(g => g.GroupId).ToHashSet();

            var row = new AiTelemetryUserRowDto
            {
                UserId = u.UserId,
                UserName = u.UserName ?? string.Empty,
                Requests = roles.Sum(r => r.Requests),
                SystemRequests = roles.Where(r => r.KeySource == "system").Sum(r => r.Requests),
                UserKeyRequests = roles.Where(r => r.KeySource == "user").Sum(r => r.Requests),
                ChatRequests = roles.Where(r => AiUsageRoles.GroupOf(r.RoleContext) == AiUsageRoles.Chat).Sum(r => r.Requests),
                TitleRequests = roles.Where(r => AiUsageRoles.GroupOf(r.RoleContext) == AiUsageRoles.TitleGeneration).Sum(r => r.Requests),
                BenchmarkRequests = roles.Where(r => AiUsageRoles.GroupOf(r.RoleContext) == AiUsageRoles.Benchmark).Sum(r => r.Requests),
                PromptTokens = roles.Sum(r => AiUsageRoles.PromptTokens(r.RoleContext, r.Input, r.CacheRead)),
                OutputTokens = roles.Sum(r => r.Output),
                EstimatedCost = roles.Sum(r => r.Cost),
                SystemEstimatedCost = roles.Where(r => r.KeySource == "system").Sum(r => r.Cost),
                UserKeyEstimatedCost = roles.Where(r => r.KeySource == "user").Sum(r => r.Cost),
                PricedRequests = roles.Sum(r => r.Priced),
                PeakHourRequests = peak?.Count ?? 0,
                PeakHourStartUtc = peak == null ? null : DateTime.SpecifyKind(peak.Day.AddHours(peak.Hour), DateTimeKind.Utc),
                RequestsPerActiveHour = hours.Count > 0 ? Math.Round((double)hours.Sum(h => h.Count) / hours.Count, 2) : 0,
                ActiveDays = hours.Select(h => h.Day).Distinct().Count(),
                LastRequestUtc = roles.Count > 0 ? DateTime.SpecifyKind(roles.Max(r => r.Last), DateTimeKind.Utc) : null,
                ChatsCreated = u.Chats,
                UserMessages = u.Messages,
                LimitRefusals = u.LimitRefusals,
                ProviderRefusals = u.ProviderRefusals,
                IsOverseerBlocked = u.Blocked,
                HasAccountHold = u.Held
            };

            var lines = new List<AiTelemetryUserConfigDto>();
            foreach (var c in u.Configs)
            {
                configs.TryGetValue(c.ConfigId, out var config);
                var assignment = assignments.FirstOrDefault(a => a.AspNetUserId == u.UserId && a.SystemAiApiConfigurationId == c.ConfigId);
                bool groupGrants = groupAssignments.Any(g => g.SystemAiApiConfigurationId == c.ConfigId && groupsOfUser.Contains(g.GroupId));

                lines.Add(new AiTelemetryUserConfigDto
                {
                    KeySource = "system",
                    ConfigId = c.ConfigId,
                    ConfigName = !string.IsNullOrEmpty(config?.DisplayName) ? config!.DisplayName : c.DisplayName ?? config?.ModelId ?? c.ConfigId.ToString(CultureInfo.InvariantCulture),
                    Provider = config?.Provider,
                    ThinkingLevel = config?.ThinkingLevel,
                    ReasoningMode = config?.ReasoningMode,
                    ServiceTier = config?.ServiceTier,
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

            // One line per UserAiModel the log attributes; a ConfigId is unique only within its key source.
            foreach (var m in u.KeyModels)
            {
                KeyModelBadges? keyModel = m.UserAiModelId is long modelId && keyModels.TryGetValue(modelId, out var found) ? found : null;
                string? modelName = keyModel?.DisplayName;
                lines.Add(new AiTelemetryUserConfigDto
                {
                    KeySource = "user",
                    ConfigId = m.UserAiModelId ?? 0,
                    ConfigName = !string.IsNullOrEmpty(modelName) ? modelName
                        : !string.IsNullOrEmpty(m.DisplayName) ? m.DisplayName
                        : m.ModelId ?? string.Empty,
                    Provider = keyModel?.Provider,
                    ThinkingLevel = keyModel?.ThinkingLevel,
                    ReasoningMode = keyModel?.ReasoningMode,
                    ServiceTier = keyModel?.ServiceTier,
                    Requests = m.Requests,
                    EstimatedCost = m.Cost,
                    LimitSource = "ownKey"
                });
            }

            row.Configs.AddRange(lines.OrderByDescending(l => l.Requests));
            rows.Add(row);
        }

        bool ascending = string.Equals(direction?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedEnumerable<AiTelemetryUserRowDto> ordered = (sort?.Trim()) switch
        {
            "requests" => SortBy(rows, r => r.Requests, ascending),
            "tokens" => SortBy(rows, r => r.PromptTokens + r.OutputTokens, ascending),
            "peakHour" => SortBy(rows, r => r.PeakHourRequests, ascending),
            "refusals" => SortBy(rows, r => r.LimitRefusals + r.ProviderRefusals, ascending),
            "lastActive" => ascending
                ? rows.OrderBy(r => r.LastRequestUtc == null).ThenBy(r => r.LastRequestUtc)
                : rows.OrderBy(r => r.LastRequestUtc == null).ThenByDescending(r => r.LastRequestUtc),
            "name" => SortBy(rows, r => r.UserName, ascending, StringComparer.OrdinalIgnoreCase),
            _ => SortBy(rows, r => r.EstimatedCost, ascending)
        };

        return new AiTelemetryUsersPageDto
        {
            KeySource = keys,
            TotalUsers = rows.Count,
            SecurityEventsAvailable = true,
            Users = ordered
                .ThenBy(r => r.UserName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.UserId, StringComparer.Ordinal)
                .Skip(skip).Take(take).ToList()
        };
    }

    // --- Groups ----------------------------------------------------------------------------------

    /// <summary>
    /// Usage per group of the selected key sources: the Users view's figures of each group's current members summed over
    /// the range, with the group's configuration lines, then "No group" (the users in no group). Every group is listed,
    /// with zeros when its members have no usage, and a member of two groups counts in both. Ordered by estimated cost,
    /// highest first, with "No group" last among equals, then by name.
    /// </summary>
    /// <param name="keySource">system (default) | user | both.</param>
    public async Task<AiTelemetryGroupsDto> GetGroupsAsync(DateTime? start, DateTime? end, string? keySource, CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        var aggregates = (await AggregateUsersAsync(start, end, keys, userId: null, search: null, ct))
            .ToDictionary(a => a.UserId, StringComparer.Ordinal);

        var groups = await _db.Groups.AsNoTracking().Select(g => new { g.Id, g.DisplayName }).ToListAsync(ct);
        var memberships = await _db.UserGroups.AsNoTracking().Select(ug => new { ug.AspNetUserId, ug.GroupId }).ToListAsync(ct);
        var membersByGroup = memberships.ToLookup(m => m.GroupId, m => m.AspNetUserId);
        var grouped = memberships.Select(m => m.AspNetUserId).ToHashSet(StringComparer.Ordinal);
        var blocked = (await _db.Users.OfType<ApplicationUser>().AsNoTracking()
            .Where(u => u.OverseerBlockedUtc != null)
            .Select(u => u.Id)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        // System configuration lines only when system usage is counted; the own-key line only when own keys are.
        var grants = IncludesSystem(keys)
            ? (await _db.GroupSystemAiApiConfigurations.AsNoTracking().ToListAsync(ct)).ToLookup(g => g.GroupId)
            : Array.Empty<GroupSystemAiApiConfiguration>().ToLookup(g => g.GroupId);
        var configIds = grants.SelectMany(g => g).Select(g => g.SystemAiApiConfigurationId)
            .Concat(aggregates.Values.SelectMany(a => a.Configs).Select(c => c.ConfigId))
            .Distinct().ToList();
        var configs = await _db.SystemAiApiConfigurations.AsNoTracking()
            .Where(c => configIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayName, c.ModelId, c.Provider, c.ThinkingLevel, c.ReasoningMode, c.ServiceTier })
            .ToDictionaryAsync(c => c.Id, ct);

        string ConfigName(long configId, string? loggedName)
        {
            configs.TryGetValue(configId, out var config);
            return !string.IsNullOrEmpty(config?.DisplayName) ? config!.DisplayName
                : loggedName ?? config?.ModelId ?? configId.ToString(CultureInfo.InvariantCulture);
        }

        AiTelemetryGroupConfigDto WithBadges(AiTelemetryGroupConfigDto line, long configId)
        {
            if (configs.TryGetValue(configId, out var config))
            {
                line.Provider = config.Provider;
                line.ThinkingLevel = config.ThinkingLevel;
                line.ReasoningMode = config.ReasoningMode;
                line.ServiceTier = config.ServiceTier;
            }
            return line;
        }

        var now = DateTime.UtcNow;
        AiTelemetryGroupRowDto Row(long? groupId, string groupName, int memberCount, int blockedMembers, List<UserAggregate> members,
            IEnumerable<GroupSystemAiApiConfiguration> granted)
        {
            var roles = members.SelectMany(m => m.Roles).ToList();

            // Members' requests summed per UTC clock hour before the busiest one is taken.
            var hours = members.SelectMany(m => m.Hours)
                .GroupBy(h => (h.Day, h.Hour))
                .Select(g => new UserHourUsage(string.Empty, g.Key.Day, g.Key.Hour, g.Sum(h => h.Count)))
                .ToList();
            var peak = hours.OrderByDescending(h => h.Count).ThenBy(h => h.Day).ThenBy(h => h.Hour).FirstOrDefault();

            var row = new AiTelemetryGroupRowDto
            {
                GroupId = groupId,
                GroupName = groupName,
                MemberCount = memberCount,
                ActiveMembers = members.Count(m => m.Roles.Count > 0),
                BlockedMembers = blockedMembers,
                Requests = roles.Sum(r => r.Requests),
                SystemRequests = roles.Where(r => r.KeySource == "system").Sum(r => r.Requests),
                UserKeyRequests = roles.Where(r => r.KeySource == "user").Sum(r => r.Requests),
                SystemEstimatedCost = roles.Where(r => r.KeySource == "system").Sum(r => r.Cost),
                UserKeyEstimatedCost = roles.Where(r => r.KeySource == "user").Sum(r => r.Cost),
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
                ChatsCreated = members.Sum(m => m.Chats),
                UserMessages = members.Sum(m => m.Messages),
                LimitRefusals = members.Sum(m => m.LimitRefusals),
                ProviderRefusals = members.Sum(m => m.ProviderRefusals)
            };

            var used = members.SelectMany(m => m.Configs)
                .GroupBy(c => c.ConfigId)
                .Select(g => new UserConfigUsage(g.Key, g.Sum(c => c.Requests), g.Sum(c => c.Cost),
                    g.Select(c => c.DisplayName).FirstOrDefault(n => !string.IsNullOrEmpty(n))))
                .ToList();
            var usedById = used.ToDictionary(c => c.ConfigId);

            // Every assignment of the group, enabled or not, with the counters it pools for its members.
            var lines = new List<AiTelemetryGroupConfigDto>();
            foreach (var grant in granted.OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
            {
                usedById.TryGetValue(grant.SystemAiApiConfigurationId, out var usage);
                lines.Add(WithBadges(new AiTelemetryGroupConfigDto
                {
                    KeySource = "system",
                    ConfigId = grant.SystemAiApiConfigurationId,
                    ConfigName = ConfigName(grant.SystemAiApiConfigurationId, usage?.DisplayName),
                    Requests = usage?.Requests ?? 0,
                    EstimatedCost = usage?.Cost ?? 0m,
                    AssignmentId = grant.Id,
                    AssignmentEnabled = grant.IsEnabled,
                    GrantedByGroup = true,
                    DailyChatRequests = EffectiveDaily(grant, now, grant.DailyChatRequestsCount),
                    MaxDailyChatRequests = grant.MaxDailyChatRequests,
                    MonthlyChatRequests = EffectiveMonthly(grant, now, grant.MonthlyChatRequestsCount),
                    MaxMonthlyChatRequests = grant.MaxMonthlyChatRequests,
                    DailyChatTokens = EffectiveDaily(grant, now, grant.DailyChatTokensCount),
                    MaxDailyChatTokens = grant.MaxDailyChatTokens
                }, grant.SystemAiApiConfigurationId));
            }

            var grantedIds = lines.Select(l => l.ConfigId).ToHashSet();
            foreach (var u in used.Where(c => !grantedIds.Contains(c.ConfigId)))
            {
                lines.Add(WithBadges(new AiTelemetryGroupConfigDto
                {
                    KeySource = "system",
                    ConfigId = u.ConfigId,
                    ConfigName = ConfigName(u.ConfigId, u.DisplayName),
                    Requests = u.Requests,
                    EstimatedCost = u.Cost
                }, u.ConfigId));
            }

            if (IncludesUser(keys))
            {
                var own = members.SelectMany(m => m.KeyModels).ToList();
                lines.Add(new AiTelemetryGroupConfigDto
                {
                    KeySource = "user",
                    ConfigName = OwnKeysLineName,
                    Requests = own.Sum(m => m.Requests),
                    EstimatedCost = own.Sum(m => m.Cost)
                });
            }

            row.Configs.AddRange(lines.OrderByDescending(l => l.Requests));
            return row;
        }

        var rows = groups
            .Select(g =>
            {
                var memberIds = membersByGroup[g.Id].Distinct(StringComparer.Ordinal).ToList();
                return Row(g.Id, g.DisplayName, memberIds.Count, memberIds.Count(blocked.Contains),
                    memberIds.Where(aggregates.ContainsKey).Select(id => aggregates[id]).ToList(), grants[g.Id]);
            })
            .ToList();

        rows.Add(Row(null, NoGroupName, await CountUngroupedAccountsAsync(ct), blocked.Count(id => !grouped.Contains(id)),
            aggregates.Values.Where(a => !grouped.Contains(a.UserId)).ToList(), Array.Empty<GroupSystemAiApiConfiguration>()));

        return new AiTelemetryGroupsDto
        {
            KeySource = keys,
            SecurityEventsAvailable = true,
            Groups = rows
                .OrderByDescending(r => r.EstimatedCost)
                .ThenBy(r => r.GroupId == null)
                .ThenBy(r => r.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.GroupId)
                .ToList()
        };
    }

    private static IOrderedEnumerable<T> SortBy<T, TKey>(IEnumerable<T> rows, Func<T, TKey> key, bool ascending,
        IComparer<TKey>? comparer = null) =>
        ascending ? rows.OrderBy(key, comparer) : rows.OrderByDescending(key, comparer);

    // --- Model users -----------------------------------------------------------------------------

    /// <summary>One user's sums of one model in one role context and thinking level.</summary>
    private sealed record ModelUserUsage(string UserId, int RoleContext, string? ThinkingLevel, long Requests, long Input, long Output,
        long CacheRead, long CacheWrite, decimal Cost, long Priced, long DurationSum, long DurationCount, DateTime Last);

    /// <summary>Usage of one model by user, in one key source and the given role groups.</summary>
    /// <param name="keySource">user reads the user-key log; anything else, <c>both</c> included, reads the system log.</param>
    /// <param name="roles">Role groups to include; null or empty: the three named groups.</param>
    public async Task<AiTelemetryModelUsersDto> GetModelUsersAsync(DateTime? start, DateTime? end, string? keySource, string provider,
        string modelId, IReadOnlyCollection<string>? roles, CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource) == "user" ? "user" : "system";
        var roleSet = new HashSet<string>(roles is { Count: > 0 } ? roles : AiUsageRoles.NamedGroups, StringComparer.OrdinalIgnoreCase);

        List<ModelUserUsage> usage;
        DateTime? costSince;
        if (keys == "user")
        {
            var q = InRange(_db.UserKeyAiUsageLogs.AsNoTracking(), start, end);
            usage = (await q
                .Where(l => l.Provider == provider && l.ModelId == modelId)
                .GroupBy(l => new { l.AspNetUserId, l.RoleContext, l.ThinkingLevel })
                .Select(g => new
                {
                    g.Key.AspNetUserId, g.Key.RoleContext, g.Key.ThinkingLevel,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    CacheWrite = g.Sum(x => (long)(x.CacheCreationInputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    DurationSum = g.Sum(x => (long)(x.TotalDurationMs ?? 0)),
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null),
                    Last = g.Max(x => x.TimestampUtc)
                })
                .ToListAsync(ct))
                .Select(a => new ModelUserUsage(a.AspNetUserId, a.RoleContext, a.ThinkingLevel, a.Requests, a.Input, a.Output, a.CacheRead,
                    a.CacheWrite, a.Cost, a.Priced, a.DurationSum, a.DurationCount, a.Last))
                .ToList();
            costSince = await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct);
        }
        else
        {
            var q = InRange(_db.SystemAiUsageLogs.AsNoTracking(), start, end);
            usage = (await q
                .Where(l => l.Provider == provider && l.ModelId == modelId)
                .GroupBy(l => new { l.AspNetUserId, l.RoleContext, l.ThinkingLevel })
                .Select(g => new
                {
                    g.Key.AspNetUserId, g.Key.RoleContext, g.Key.ThinkingLevel,
                    Requests = g.LongCount(),
                    Input = g.Sum(x => (long)(x.InputTokens ?? 0)),
                    Output = g.Sum(x => (long)(x.OutputTokens ?? 0)),
                    CacheRead = g.Sum(x => (long)(x.CacheReadInputTokens ?? 0)),
                    CacheWrite = g.Sum(x => (long)(x.CacheCreationInputTokens ?? 0)),
                    Cost = g.Sum(x => x.EstimatedCost ?? 0m),
                    Priced = g.LongCount(x => x.EstimatedCost != null),
                    DurationSum = g.Sum(x => (long)(x.TotalDurationMs ?? 0)),
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null),
                    Last = g.Max(x => x.TimestampUtc)
                })
                .ToListAsync(ct))
                .Select(a => new ModelUserUsage(a.AspNetUserId, a.RoleContext, a.ThinkingLevel, a.Requests, a.Input, a.Output, a.CacheRead,
                    a.CacheWrite, a.Cost, a.Priced, a.DurationSum, a.DurationCount, a.Last))
                .ToList();
            costSince = await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct);
        }

        usage = usage.Where(u => roleSet.Contains(AiUsageRoles.GroupOf(u.RoleContext))).ToList();

        var ids = usage.Select(u => u.UserId).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<string, string?>()
            : await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, ct);

        var rows = usage
            .Select(u => new AiTelemetryModelUserRowDto
            {
                UserId = u.UserId,
                UserName = names.TryGetValue(u.UserId, out var name) ? name : null,
                RoleContext = u.RoleContext,
                RoleGroup = AiUsageRoles.GroupOf(u.RoleContext),
                ThinkingLevel = u.ThinkingLevel,
                Requests = u.Requests,
                PromptTokens = AiUsageRoles.PromptTokens(u.RoleContext, u.Input, u.CacheRead),
                UncachedInputTokens = AiUsageRoles.UncachedInputTokens(u.RoleContext, u.Input, u.CacheRead, u.CacheWrite),
                CacheReadTokens = u.CacheRead,
                CacheCreationTokens = u.CacheWrite,
                OutputTokens = u.Output,
                DurationSumMs = u.DurationSum,
                DurationCount = u.DurationCount,
                EstimatedCost = u.Cost,
                PricedRequests = u.Priced,
                LastRequestUtc = DateTime.SpecifyKind(u.Last, DateTimeKind.Utc)
            })
            .OrderByDescending(r => r.EstimatedCost)
            .ThenBy(r => r.UserName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.UserId, StringComparer.Ordinal)
            .ThenBy(r => r.RoleContext)
            .ThenBy(r => r.ThinkingLevel ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        return new AiTelemetryModelUsersDto
        {
            KeySource = keys,
            Provider = provider,
            ModelId = modelId,
            ModelName = ModelName(provider, modelId, new Dictionary<(string, string), string?>()),
            Roles = AiUsageRoles.NamedGroups.Concat(new[] { AiUsageRoles.Other })
                .Where(roleSet.Contains).ToList(),
            CostRecordedSinceUtc = Utc(costSince),
            Rows = rows
        };
    }

    // --- User and group models -------------------------------------------------------------------

    /// <summary>The audience's sums of one model in one key source, role context and thinking level.</summary>
    private sealed record UserModelUsage(string KeySource, string Provider, string ModelId, int RoleContext, string? ThinkingLevel,
        long Requests, long Input, long Output, long CacheRead, long CacheWrite, decimal Cost, long Priced, long DurationSum,
        long DurationCount, DateTime Last);

    /// <summary>
    /// The audience's usage by key source, provider, model, role context and thinking level, in the selected key sources and
    /// every role group, with the earliest priced row of the included logs in the range (of every user).
    /// </summary>
    private async Task<(List<AiTelemetryUserModelRowDto> Rows, DateTime? CostSince)> ModelRowsAsync(DateTime? start, DateTime? end,
        string keys, Audience audience, CancellationToken ct)
    {
        var usage = new List<UserModelUsage>();
        DateTime? costSince = null;

        if (IncludesSystem(keys))
        {
            var q = InRange(_db.SystemAiUsageLogs.AsNoTracking(), start, end);
            usage.AddRange((await ApplyAudience(q, l => l.AspNetUserId, audience)
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
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null),
                    Last = g.Max(x => x.TimestampUtc)
                })
                .ToListAsync(ct))
                .Select(a => new UserModelUsage("system", a.Provider, a.ModelId, a.RoleContext, a.ThinkingLevel, a.Requests, a.Input,
                    a.Output, a.CacheRead, a.CacheWrite, a.Cost, a.Priced, a.DurationSum, a.DurationCount, a.Last)));
            costSince = Earliest(costSince, await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct));
        }

        if (IncludesUser(keys))
        {
            var q = InRange(_db.UserKeyAiUsageLogs.AsNoTracking(), start, end);
            usage.AddRange((await ApplyAudience(q, l => l.AspNetUserId, audience)
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
                    DurationCount = g.LongCount(x => x.TotalDurationMs != null),
                    Last = g.Max(x => x.TimestampUtc)
                })
                .ToListAsync(ct))
                .Select(a => new UserModelUsage("user", a.Provider, a.ModelId, a.RoleContext, a.ThinkingLevel, a.Requests, a.Input,
                    a.Output, a.CacheRead, a.CacheWrite, a.Cost, a.Priced, a.DurationSum, a.DurationCount, a.Last)));
            costSince = Earliest(costSince, await q.Where(l => l.EstimatedCost != null).MinAsync(l => (DateTime?)l.TimestampUtc, ct));
        }

        var modelNames = new Dictionary<(string, string), string?>();
        var rows = usage
            .Select(u => new AiTelemetryUserModelRowDto
            {
                KeySource = u.KeySource,
                Provider = u.Provider,
                ModelId = u.ModelId,
                ModelName = ModelName(u.Provider, u.ModelId, modelNames),
                RoleContext = u.RoleContext,
                RoleGroup = AiUsageRoles.GroupOf(u.RoleContext),
                ThinkingLevel = u.ThinkingLevel,
                Requests = u.Requests,
                PromptTokens = AiUsageRoles.PromptTokens(u.RoleContext, u.Input, u.CacheRead),
                UncachedInputTokens = AiUsageRoles.UncachedInputTokens(u.RoleContext, u.Input, u.CacheRead, u.CacheWrite),
                CacheReadTokens = u.CacheRead,
                CacheCreationTokens = u.CacheWrite,
                OutputTokens = u.Output,
                DurationSumMs = u.DurationSum,
                DurationCount = u.DurationCount,
                EstimatedCost = u.Cost,
                PricedRequests = u.Priced,
                LastRequestUtc = DateTime.SpecifyKind(u.Last, DateTimeKind.Utc)
            })
            .OrderByDescending(r => r.EstimatedCost)
            .ThenBy(r => r.ModelName ?? r.ModelId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RoleContext)
            .ThenBy(r => r.ThinkingLevel, StringComparer.Ordinal)
            .ThenBy(r => r.KeySource, StringComparer.Ordinal)
            .ThenBy(r => r.Provider, StringComparer.Ordinal)
            .ToList();

        return (rows, costSince);
    }

    /// <summary>Usage of one user by model, role context and thinking level, in the selected key sources and every role group.</summary>
    /// <param name="keySource">system (default) | user | both.</param>
    public async Task<AiTelemetryUserModelsDto> GetUserModelsAsync(DateTime? start, DateTime? end, string? keySource, string userId,
        CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        var (rows, costSince) = await ModelRowsAsync(start, end, keys, Audience.OfUser(userId), ct);
        string? userName = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);

        return new AiTelemetryUserModelsDto
        {
            KeySource = keys,
            UserId = userId,
            UserName = userName,
            CostRecordedSinceUtc = Utc(costSince),
            Rows = rows
        };
    }

    /// <summary>
    /// Usage of one group's current members, or of the users in no group, by model, role context and thinking level, in
    /// the selected key sources and every role group. Null when no group has the id.
    /// </summary>
    /// <param name="keySource">system (default) | user | both.</param>
    /// <param name="groupId">The group; null is "No group".</param>
    public async Task<AiTelemetryGroupModelsDto?> GetGroupModelsAsync(DateTime? start, DateTime? end, string? keySource, long? groupId,
        CancellationToken ct = default)
    {
        string keys = NormalizeKeySource(keySource);
        string groupName;
        int memberCount;
        if (groupId is long id)
        {
            var group = await _db.Groups.AsNoTracking().Where(g => g.Id == id).Select(g => new { g.DisplayName }).FirstOrDefaultAsync(ct);
            if (group == null) return null;
            groupName = group.DisplayName;
            memberCount = await _db.UserGroups.CountAsync(ug => ug.GroupId == id, ct);
        }
        else
        {
            groupName = NoGroupName;
            memberCount = await CountUngroupedAccountsAsync(ct);
        }

        var (rows, costSince) = await ModelRowsAsync(start, end, keys, Audience.OfGroup(groupId), ct);
        return new AiTelemetryGroupModelsDto
        {
            KeySource = keys,
            GroupId = groupId,
            GroupName = groupName,
            MemberCount = memberCount,
            CostRecordedSinceUtc = Utc(costSince),
            Rows = rows
        };
    }

    /// <summary>A daily counter as the next request would see it: 0 when its last reset was before today (UTC).</summary>
    private static int EffectiveDaily(IRateLimitedEntity entity, DateTime now, int count) =>
        entity.LastDailyReset == null || entity.LastDailyReset.Value.Date < now.Date ? 0 : count;

    /// <summary>A daily token counter as the next request would see it.</summary>
    private static long EffectiveDaily(IRateLimitedEntity entity, DateTime now, long count) =>
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
