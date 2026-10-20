namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.RootCause;

/// <summary>
/// What the Report Pack preview and Start's pre-flight read from one preparation
/// (<see cref="BenchmarkReportPackPreparation"/>), and each offered document's prompt length; never the
/// preparation itself. Model scope: one per subject. The entry DTOs are shared and are not changed.
/// </summary>
public sealed class BenchmarkReportPackPreviewEntry
{
    public BenchmarkReportScope Scope { get; init; } = BenchmarkReportScope.Model;

    /// <summary>Model scope: the subject; comparison scope: the first covered model in letter order.</summary>
    public BenchmarkModelComparisonEntryDto Subject { get; init; } = default!;

    /// <summary>Model scope: the subject has a peer in the comparison.</summary>
    public bool HasPeers { get; init; }

    public string SheetSubjectKey { get; init; } = string.Empty;
    public string SubjectLabel { get; init; } = string.Empty;
    public string SubjectState { get; init; } = string.Empty;
    public long? SuiteId { get; init; }
    public string SuiteName { get; init; } = string.Empty;
    public IReadOnlyList<BenchmarkReportPackPeerDto> Peers { get; init; } = Array.Empty<BenchmarkReportPackPeerDto>();

    /// <summary>Comparison scope: the sheet's peers in letter order, each with its letter.</summary>
    public IReadOnlyList<BenchmarkReportCoveredModelDto> CoveredModels { get; init; } = Array.Empty<BenchmarkReportCoveredModelDto>();

    public IReadOnlyList<BenchmarkModelComparisonEntryDto> Covered { get; init; } = Array.Empty<BenchmarkModelComparisonEntryDto>();
    public IReadOnlyList<string> CoveredEntryKeys { get; init; } = Array.Empty<string>();
    public string CoveredSetKey { get; init; } = string.Empty;
    public bool CoversAllEntries { get; init; }
    public int ComparisonEntryCount { get; init; }

    /// <summary>The comparison's entries that are not Excluded, as the preview counts them.</summary>
    public int ComparableEntryCount { get; init; }

    public string? ReferenceEntryKey { get; init; }

    /// <summary>Each offered document's system prompt plus user message length, in characters.</summary>
    public IReadOnlyDictionary<BenchmarkReportAudience, int> PromptChars { get; init; } = new Dictionary<BenchmarkReportAudience, int>();
}

/// <summary>
/// The outcome of one comparison-scope preview preparation run by
/// <see cref="BenchmarkReportPackPreviewCache.GetOrPrepareAsync"/>: the entry, or the refusal instead.
/// </summary>
public sealed record BenchmarkReportPackPreviewPreparationResult
{
    public BenchmarkReportPackPreviewEntry? Entry { get; init; }
    public string? Refusal { get; init; }

    /// <summary>The comparison's entries that are not Excluded; null when the comparison could not be computed.</summary>
    public int? ComparableEntryCount { get; init; }

    /// <summary>A refusal of the covered models, the reference or the preparation, shown with the comparison's other covered sets.</summary>
    public bool CoveredRefusal { get; init; }

    public long CompareMs { get; init; }
    public long PrepareMs { get; init; }
    public BenchmarkReportPackPrepareTimings? Timings { get; init; }

    /// <summary>The waiter joined a preparation another request started.</summary>
    public bool Joined { get; init; }

    /// <summary>The entry was already in the cache; nothing was prepared.</summary>
    public bool Cached { get; init; }
}

/// <summary>
/// The Report Pack preview's preparations (<see cref="BenchmarkReportPackPreviewEntry"/>), kept for
/// 15 minutes so a writer or checkbox change does not compute the comparison, the paired tests, the
/// fact sheet and the prompts again. The key (<see cref="KeyAsync"/>) is a SHA-256 over the request
/// without its writer, documents, acknowledgment and replacements, the settings the preparation reads,
/// the code versions, and every changeable database value the comparison and the preparation read; an
/// entry can still be up to 15 minutes stale for anything the key does not cover. A job always prepares
/// fresh. A singleton over the global <see cref="IMemoryCache"/>, one size unit per entry. A comparison-scope
/// preparation (<see cref="GetOrPrepareAsync"/>) runs once per key at a time, and outlives the request
/// that started it up to its timeout.
/// </summary>
public sealed class BenchmarkReportPackPreviewCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>The seconds a preparation started by <see cref="GetOrPrepareAsync"/> may take without <c>Benchmark:ReportPack:PreviewPreparationTimeoutSeconds</c>.</summary>
    public const int DefaultPreparationTimeoutSeconds = 180;

    private const string KeyPrefix = "benchmark-report-pack-preview:";

    private readonly IMemoryCache _cache;
    private readonly ILogger<BenchmarkReportPackPreviewCache> _logger;

    /// <summary>The preparations in flight, one per key.</summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<BenchmarkReportPackPreviewPreparationResult>>> _inFlight =
        new(StringComparer.Ordinal);

    private int _joinedWaiters;

    public BenchmarkReportPackPreviewCache(IMemoryCache cache, ILogger<BenchmarkReportPackPreviewCache>? logger = null)
    {
        _cache = cache;
        _logger = logger ?? NullLogger<BenchmarkReportPackPreviewCache>.Instance;
    }

    /// <summary>Awaited at the start of each preparation with its timeout token; null outside tests.</summary>
    internal Func<CancellationToken, Task>? BeforePrepare { get; set; }

    /// <summary>How many waiters have joined a preparation already in flight.</summary>
    internal int JoinedWaiters => Volatile.Read(ref _joinedWaiters);

    /// <summary>The preparations in flight.</summary>
    internal int InFlightCount => _inFlight.Count;

    public bool TryGet(string key, out BenchmarkReportPackPreviewEntry? entry)
        => _cache.TryGetValue(KeyPrefix + key, out entry) && entry != null;

    public void Set(string key, BenchmarkReportPackPreviewEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _cache.Set(KeyPrefix + key, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = 1
        });
    }

    /// <summary>The refusal of a preparation stopped by its timeout.</summary>
    public static string TimeoutRefusal(TimeSpan timeout)
        => $"The estimate took longer than {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} seconds to prepare and was stopped. Try again.";

    /// <summary>
    /// The entry of <paramref name="key"/> from the cache; else the result of the preparation in flight
    /// for it; else the result of <paramref name="prepare"/>, started on a token canceled after
    /// <paramref name="timeout"/>, never on <paramref name="requestToken"/>. An entry is stored; a
    /// refusal, a timeout (<see cref="TimeoutRefusal"/>) and an exception reach every waiter and are not
    /// stored. <paramref name="requestToken"/> cancels only this caller's wait: the preparation goes on
    /// and stores its entry. The in-flight slot is freed when the preparation ends, whatever its outcome.
    /// </summary>
    public async Task<BenchmarkReportPackPreviewPreparationResult> GetOrPrepareAsync(
        string key,
        Func<CancellationToken, Task<BenchmarkReportPackPreviewPreparationResult>> prepare,
        TimeSpan timeout,
        CancellationToken requestToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(prepare);
        if (TryGet(key, out var cached)) return CachedResult(cached!);

        Lazy<Task<BenchmarkReportPackPreviewPreparationResult>> created = null!;
        created = new Lazy<Task<BenchmarkReportPackPreviewPreparationResult>>(
            () => Observed(Task.Run(() => PrepareCoreAsync(key, prepare, timeout, created))),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var slot = _inFlight.GetOrAdd(key, created);
        bool joined = !ReferenceEquals(slot, created);
        if (joined) Interlocked.Increment(ref _joinedWaiters);

        var result = await slot.Value.WaitAsync(requestToken).ConfigureAwait(false);
        return joined && !result.Joined ? result with { Joined = true } : result;
    }

    private static BenchmarkReportPackPreviewPreparationResult CachedResult(BenchmarkReportPackPreviewEntry entry)
        => new() { Entry = entry, ComparableEntryCount = entry.ComparableEntryCount, Cached = true };

    /// <summary>The task, its exception observed even when every waiter has stopped waiting; it is logged where it is thrown.</summary>
    private static Task<BenchmarkReportPackPreviewPreparationResult> Observed(Task<BenchmarkReportPackPreviewPreparationResult> task)
    {
        task.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    /// <summary>One preparation of <paramref name="key"/>, run once for <paramref name="slot"/> and its waiters.</summary>
    private async Task<BenchmarkReportPackPreviewPreparationResult> PrepareCoreAsync(
        string key,
        Func<CancellationToken, Task<BenchmarkReportPackPreviewPreparationResult>> prepare,
        TimeSpan timeout,
        Lazy<Task<BenchmarkReportPackPreviewPreparationResult>> slot)
    {
        try
        {
            // An entry stored between the caller's cache read and its slot.
            if (TryGet(key, out var stored)) return CachedResult(stored!);

            using var timeoutSource = new CancellationTokenSource(timeout);
            try
            {
                if (BeforePrepare is { } before) await before(timeoutSource.Token).ConfigureAwait(false);
                var result = await prepare(timeoutSource.Token).ConfigureAwait(false);
                if (result.Entry != null) Set(key, result.Entry);
                return result;
            }
            catch (Exception ex) when (ex.IsCanceledBy(timeoutSource.Token))
            {
                _logger.LogWarning("Report Pack preview preparation stopped after its timeout of {Timeout}.", timeout);
                return new BenchmarkReportPackPreviewPreparationResult { Refusal = TimeoutRefusal(timeout) };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Report Pack preview preparation failed.");
            throw;
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<BenchmarkReportPackPreviewPreparationResult>>>(key, slot));
        }
    }

    /// <summary>
    /// The cache key of one preparation: <paramref name="subjectKey"/> for a model-scope subject, null for
    /// the comparison scope, whose covered models and reference come from the request. Covers the
    /// request's scope, sources in order, pricing basis, covered models and reference; today's UTC date
    /// on the Current basis; <paramref name="answerExcerptChars"/>, the battery detail and prompt limits,
    /// the Report Pack settings and the recommended models; the report format versions and <see cref="RootCauseEngine.Version"/>; the
    /// numbered comparison's id; the latest baseline model setting; each group's name and members; each
    /// battery run's state, members and latest analysis; each source run's scoring, synthesis
    /// fingerprint, status, re-run, re-assessment, repair and provider-ledger figures and its answers'
    /// count, quality sum and latest grading and re-run times; and the enabled Benchmark-role
    /// configurations with the configurations the runs name, with their pricing.
    /// </summary>
    public static async Task<string> KeyAsync(
        ApplicationDbContext db,
        BenchmarkReportPackRequest request,
        string? subjectKey,
        int answerExcerptChars,
        int? comparisonId,
        IConfiguration? configuration,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);

        var runIds = (request.RunIds ?? new List<long>()).Distinct().ToList();
        var groupIds = (request.GroupIds ?? new List<long>()).Distinct().ToList();
        var batteryRunIds = (request.BatteryRunIds ?? new List<long>()).Distinct().ToList();

        var key = new StringBuilder(1024);
        void Part(string name, object? value)
            => key.Append(name).Append('=').Append(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null").Append('\n');
        static string Time(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? "null";
        static string Joined(params object?[] values)
            => string.Join("|", values.Select(v => v == null ? "null" : Convert.ToString(v, CultureInfo.InvariantCulture)));

        Part("scope", request.Scope);
        Part("runs", string.Join(",", runIds));
        Part("groups", string.Join(",", groupIds));
        Part("batteries", string.Join(",", batteryRunIds));
        Part("basis", request.PricingBasis);
        if (request.PricingBasis == BenchmarkModelComparisonPricingBasis.Current)
        {
            Part("today", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        Part("subject", subjectKey);
        if (request.Scope == BenchmarkReportScope.Comparison)
        {
            Part("covered", string.Join(",", request.CoveredEntryKeys ?? new List<string>()));
            Part("reference", request.ReferenceEntryKey);
        }
        Part("excerpt", answerExcerptChars);
        Part("batteryDetail", BenchmarkReportPackPreparation.BatteryDetailQuestionsPerSuite(configuration));
        Part("batteryPrompt", BenchmarkReportPackPreparation.BatteryMaxPromptChars(configuration));
        Part("format", BenchmarkReportPackRenderer.ReportFormatVersion);
        Part("comparisonFormat", BenchmarkReportPackRenderer.ComparisonReportFormatVersion);
        Part("engine", RootCauseEngine.Version);
        Part("comparisonId", comparisonId);
        if (configuration != null)
        {
            foreach (var section in new[] { "Benchmark:ReportPack", "RecommendedModels" })
            {
                foreach (var setting in configuration.GetSection(section).AsEnumerable().OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    Part("setting:" + setting.Key, setting.Value);
                }
            }
        }

        var baseline = await db.BenchmarkBaselineModelSettings
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .OrderByDescending(s => s.Id)
            .Select(s => new { s.Id, s.ModelSnapshotId })
            .FirstOrDefaultAsync(ct);
        Part("baseline", baseline == null ? null : baseline.Id + ":" + baseline.ModelSnapshotId);

        var sourceRunIds = new List<long>(runIds);
        if (groupIds.Count > 0)
        {
            var groups = await db.BenchmarkRunGroups
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(g => groupIds.Contains(g.Id))
                .OrderBy(g => g.Id)
                .Select(g => new { g.Id, g.Name, Members = g.Members.Select(m => m.BenchmarkRunId).ToList() })
                .ToListAsync(ct);
            foreach (var group in groups)
            {
                var members = group.Members.OrderBy(id => id).ToList();
                Part("group:" + group.Id, group.Name + "|" + string.Join(",", members));
                sourceRunIds.AddRange(members);
            }
        }

        if (batteryRunIds.Count > 0)
        {
            var batteries = await db.BenchmarkBatteryRuns
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(b => batteryRunIds.Contains(b.Id))
                .OrderBy(b => b.Id)
                .Select(b => new
                {
                    b.Id, b.Status, b.CompletedAtUtc, b.CompletedMemberCount, b.FailedMemberCount,
                    Members = b.Members.Select(m => new { m.BenchmarkRunId, m.Superseded, m.SuiteIndex, m.Round }).ToList()
                })
                .ToListAsync(ct);
            var analyses = await db.BenchmarkBatteryAnalyses
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(a => batteryRunIds.Contains(a.BenchmarkBatteryRunId))
                .Select(a => new { a.BenchmarkBatteryRunId, a.Id, a.ComputedAtUtc })
                .ToListAsync(ct);
            foreach (var battery in batteries)
            {
                var own = analyses.Where(a => a.BenchmarkBatteryRunId == battery.Id).ToList();
                var members = battery.Members.OrderBy(m => m.BenchmarkRunId).ToList();
                Part("battery:" + battery.Id, Joined(
                    battery.Status, Time(battery.CompletedAtUtc), battery.CompletedMemberCount, battery.FailedMemberCount,
                    own.Count == 0 ? "null" : own.Max(a => a.Id).ToString(CultureInfo.InvariantCulture),
                    own.Count == 0 ? "null" : Time(own.Max(a => a.ComputedAtUtc)),
                    string.Join(",", members.Select(m => m.BenchmarkRunId + (m.Superseded ? "s" : string.Empty) + "@" + m.SuiteIndex + "." + m.Round))));
                sourceRunIds.AddRange(members.Select(m => m.BenchmarkRunId));
            }
        }

        var ids = sourceRunIds.Distinct().ToList();
        var runs = await db.BenchmarkRuns
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(r => ids.Contains(r.Id))
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id, r.ScoringMethodVersion, r.RerunCompletedAtUtc, r.FinalScore, r.QualityIndex, r.SpeedIndex, r.SynthesisFingerprint,
                r.Status, r.CompletedAtUtc, r.CoAssessorFinalScore, r.UnweightedQualityIndex, r.ReassessedAnswerCount,
                r.RepairPassCount, r.ProviderRepairPassCount, r.ProviderLedgerVersion, r.CallTelemetryVersion,
                r.CandidateValidAttemptCount, r.CandidateProviderErrorCount, r.CandidateUnattributedErrorCount, r.CandidateOurSideFailureCount,
                r.ReplacesRunId,
                r.TestedModelConfigurationId, r.AssessorModelConfigurationId, r.CoAssessorModelConfigurationId,
                r.SecondOpinionAssessorModelConfigurationId, r.ClaimVerifierModelConfigurationId,
                AnswerCount = r.Answers.Count(),
                QualitySum = r.Answers.Sum(a => a.QualityScore),
                LastAssessedAtUtc = r.Answers.Max(a => a.AssessedAtUtc),
                LastCoAssessedAtUtc = r.Answers.Max(a => a.CoAssessedAtUtc),
                LastReassessedAtUtc = r.Answers.Max(a => a.ReassessedAtUtc),
                LastRerunAtUtc = r.Answers.Max(a => a.RerunAtUtc)
            })
            .ToListAsync(ct);
        foreach (var run in runs)
        {
            Part("run:" + run.Id, Joined(
                run.ScoringMethodVersion, Time(run.RerunCompletedAtUtc), run.FinalScore, run.QualityIndex, run.SpeedIndex, run.SynthesisFingerprint,
                run.Status, Time(run.CompletedAtUtc), run.CoAssessorFinalScore, run.UnweightedQualityIndex, run.ReassessedAnswerCount,
                run.RepairPassCount, run.ProviderRepairPassCount, run.ProviderLedgerVersion, run.CallTelemetryVersion,
                run.CandidateValidAttemptCount, run.CandidateProviderErrorCount, run.CandidateUnattributedErrorCount, run.CandidateOurSideFailureCount,
                run.ReplacesRunId, run.AnswerCount, run.QualitySum,
                Time(run.LastAssessedAtUtc), Time(run.LastCoAssessedAtUtc), Time(run.LastReassessedAtUtc), Time(run.LastRerunAtUtc)));
        }

        var named = runs
            .SelectMany(r => new[]
            {
                r.TestedModelConfigurationId, r.AssessorModelConfigurationId, r.CoAssessorModelConfigurationId,
                r.SecondOpinionAssessorModelConfigurationId, r.ClaimVerifierModelConfigurationId
            })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var configurations = await db.SystemAiApiConfigurations
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(c => (c.IsEnabled && (c.ModelRole & RootCauseNextRuns.BenchmarkRoleBit) != 0) || named.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id, c.IsEnabled, c.ModelRole, c.OrderIndex, c.Provider, c.ModelId, c.ThinkingLevel, c.DisplayName,
                c.PricingMode, c.InputPricePerMillion, c.OutputPricePerMillion, c.CachedInputPricePerMillion
            })
            .ToListAsync(ct);
        foreach (var c in configurations)
        {
            Part("configuration:" + c.Id, Joined(
                c.IsEnabled, c.ModelRole, c.OrderIndex, c.Provider, c.ModelId, c.ThinkingLevel, c.DisplayName,
                c.PricingMode, c.InputPricePerMillion, c.OutputPricePerMillion, c.CachedInputPricePerMillion));
        }

        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
