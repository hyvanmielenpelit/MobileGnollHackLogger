namespace Overseer.Services.Feedback;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;

/// <summary>
/// The status of a GnollBench feedback issue as the admin view shows it: the stored triage status, with
/// <see cref="Regressed"/> derived when an accepted self-report is newer than the resolution, and
/// <see cref="New"/> when no triage row exists.
/// </summary>
public enum GnollBenchFeedbackStatus
{
    New,
    Acknowledged,
    Resolved,
    Regressed,
    Dismissed,
}

/// <summary>Which self-reports form the base set: accepted calls (the default), refused calls, or both.</summary>
public enum GnollBenchAcceptedScope
{
    Accepted,
    All,
    Refused,
}

/// <summary>The lowercase wire values of <see cref="GnollBenchFeedbackStatus"/> and <see cref="GnollBenchAcceptedScope"/>.</summary>
public static class GnollBenchFeedbackStatuses
{
    public const string New = "new";
    public const string Acknowledged = "acknowledged";
    public const string Resolved = "resolved";
    public const string Regressed = "regressed";
    public const string Dismissed = "dismissed";

    public const string ScopeAccepted = "accepted";
    public const string ScopeAll = "all";
    public const string ScopeRefused = "refused";

    public static string ToWire(GnollBenchFeedbackStatus status) => status switch
    {
        GnollBenchFeedbackStatus.Acknowledged => Acknowledged,
        GnollBenchFeedbackStatus.Resolved => Resolved,
        GnollBenchFeedbackStatus.Regressed => Regressed,
        GnollBenchFeedbackStatus.Dismissed => Dismissed,
        _ => New
    };

    /// <summary>Parses a wire value, ignoring case and surrounding whitespace.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out GnollBenchFeedbackStatus status)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case New: status = GnollBenchFeedbackStatus.New; return true;
            case Acknowledged: status = GnollBenchFeedbackStatus.Acknowledged; return true;
            case Resolved: status = GnollBenchFeedbackStatus.Resolved; return true;
            case Regressed: status = GnollBenchFeedbackStatus.Regressed; return true;
            case Dismissed: status = GnollBenchFeedbackStatus.Dismissed; return true;
            default: status = GnollBenchFeedbackStatus.New; return false;
        }
    }

    /// <summary>Parses the <c>accepted</c> parameter; null and empty mean <see cref="GnollBenchAcceptedScope.Accepted"/>.</summary>
    public static bool TryParseScope(string? value, out GnollBenchAcceptedScope scope)
    {
        switch (string.IsNullOrWhiteSpace(value) ? ScopeAccepted : value.Trim().ToLowerInvariant())
        {
            case ScopeAccepted: scope = GnollBenchAcceptedScope.Accepted; return true;
            case ScopeAll: scope = GnollBenchAcceptedScope.All; return true;
            case ScopeRefused: scope = GnollBenchAcceptedScope.Refused; return true;
            default: scope = GnollBenchAcceptedScope.Accepted; return false;
        }
    }

    /// <summary>
    /// The shown status: <see cref="GnollBenchFeedbackStatus.New"/> without a triage row, and
    /// <see cref="GnollBenchFeedbackStatus.Regressed"/> when the row is Resolved and
    /// <paramref name="lastAcceptedUtc"/> is newer than its <see cref="BenchmarkFeedbackTriage.ResolvedUtc"/>.
    /// </summary>
    public static GnollBenchFeedbackStatus Derive(BenchmarkFeedbackTriage? triage, DateTime? lastAcceptedUtc)
    {
        if (triage == null) return GnollBenchFeedbackStatus.New;
        return triage.Status switch
        {
            BenchmarkFeedbackTriageStatus.Acknowledged => GnollBenchFeedbackStatus.Acknowledged,
            BenchmarkFeedbackTriageStatus.Resolved =>
                triage.ResolvedUtc.HasValue && lastAcceptedUtc.HasValue && lastAcceptedUtc.Value > triage.ResolvedUtc.Value
                    ? GnollBenchFeedbackStatus.Regressed
                    : GnollBenchFeedbackStatus.Resolved,
            BenchmarkFeedbackTriageStatus.Dismissed => GnollBenchFeedbackStatus.Dismissed,
            _ => GnollBenchFeedbackStatus.New
        };
    }
}

/// <summary>
/// The parsed filters, sort and base scope of a GnollBench feedback query. Occurrence-level filters select an issue
/// when one self-report of the base set matches all of them together; <see cref="Statuses"/> and the date range apply
/// to the issue.
/// </summary>
public sealed class GnollBenchFeedbackFilter
{
    public const int MaxSearchLength = 200;
    public const int MaxFingerprints = 500;

    /// <summary>The <c>corpus</c> value that selects runs with no corpus condition.</summary>
    public const string LiveCorpus = "live";

    /// <summary>The <c>variant</c> value that selects runs with no instruction variant.</summary>
    public const string NoVariant = "none";

    public const string SortLastSeen = "lastSeen";
    public const string SortFirstSeen = "firstSeen";
    public const string SortCount = "count";
    public const string SortRuns = "runs";
    public const string SortModels = "models";
    public const string SortSubject = "subject";

    public static IReadOnlyList<string> Sorts { get; } = new[] { SortLastSeen, SortFirstSeen, SortCount, SortRuns, SortModels, SortSubject };

    /// <summary>Inclusive; compared with the issue's last-seen time.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Includes its whole day; compared with the issue's first-seen time.</summary>
    public DateTime? EndDate { get; set; }

    public GnollBenchAcceptedScope Accepted { get; set; } = GnollBenchAcceptedScope.Accepted;
    public List<GnollBenchFeedbackStatus> Statuses { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<string> Models { get; set; } = new();
    public List<string> Providers { get; set; } = new();
    public List<long> Runs { get; set; } = new();
    public List<long> Series { get; set; } = new();
    public List<long> Groups { get; set; } = new();
    public List<long> Batteries { get; set; } = new();
    public List<long> Batches { get; set; } = new();
    public List<long> Suites { get; set; } = new();
    public List<long> Questions { get; set; } = new();
    public List<BenchmarkInformationCondition> Conditions { get; set; } = new();

    /// <summary>Stored corpus conditions; <see cref="LiveCorpus"/> matches a null one.</summary>
    public List<string> Corpora { get; set; } = new();

    /// <summary>Variant names without the hash; <see cref="NoVariant"/> matches a run without one.</summary>
    public List<string> Variants { get; set; } = new();
    public List<string> Harnesses { get; set; } = new();
    public List<string> Fingerprints { get; set; } = new();
    public string? Search { get; set; }
    public string Sort { get; set; } = SortLastSeen;

    /// <summary>True when any filter applies to the self-reports rather than to the issue.</summary>
    public bool HasOccurrenceFilters =>
        Categories.Count > 0 || Models.Count > 0 || Providers.Count > 0 || Runs.Count > 0 || Series.Count > 0
        || Groups.Count > 0 || Batteries.Count > 0 || Batches.Count > 0 || Suites.Count > 0 || Questions.Count > 0
        || Conditions.Count > 0 || Corpora.Count > 0 || Variants.Count > 0 || Harnesses.Count > 0
        || Fingerprints.Count > 0 || Search != null;

    /// <summary>
    /// Parses the query parameters. Every list is comma-separated; an unknown status, scope, category, condition or
    /// sort, a non-numeric id, more than <see cref="MaxFingerprints"/> fingerprints or a search longer than
    /// <see cref="MaxSearchLength"/> characters is an error.
    /// </summary>
    public static bool TryParse(GnollBenchFeedbackFilterQuery? query, out GnollBenchFeedbackFilter filter, [NotNullWhen(false)] out string? error)
    {
        query ??= new GnollBenchFeedbackFilterQuery();
        filter = new GnollBenchFeedbackFilter { StartDate = query.StartDate, EndDate = query.EndDate };
        error = null;

        if (!GnollBenchFeedbackStatuses.TryParseScope(query.Accepted, out var scope))
        {
            error = $"Unknown accepted value '{query.Accepted}'. Use accepted, all or refused.";
            return false;
        }
        filter.Accepted = scope;

        foreach (var part in Split(query.Status))
        {
            if (!GnollBenchFeedbackStatuses.TryParse(part, out var status)) { error = $"Unknown status '{part}'."; return false; }
            if (!filter.Statuses.Contains(status)) filter.Statuses.Add(status);
        }

        foreach (var part in Split(query.Category))
        {
            if (!FeedbackCategories.IsKnown(part)) { error = $"Unknown category '{part}'."; return false; }
            AddDistinct(filter.Categories, part);
        }

        foreach (var part in Split(query.Condition))
        {
            if (!BenchmarkInformationConditions.TryParse(part, out var condition)) { error = $"Unknown condition '{part}'."; return false; }
            if (!filter.Conditions.Contains(condition)) filter.Conditions.Add(condition);
        }

        foreach (var part in Split(query.Model)) AddDistinct(filter.Models, part);
        foreach (var part in Split(query.Provider)) AddDistinct(filter.Providers, part);
        foreach (var part in Split(query.Corpus)) AddDistinct(filter.Corpora, part);
        foreach (var part in Split(query.Variant)) AddDistinct(filter.Variants, part);
        foreach (var part in Split(query.Harness)) AddDistinct(filter.Harnesses, part);
        foreach (var part in Split(query.Fingerprints)) AddDistinct(filter.Fingerprints, part.ToLowerInvariant());
        if (filter.Fingerprints.Count > MaxFingerprints)
        {
            error = $"At most {MaxFingerprints} fingerprints can be given.";
            return false;
        }

        if (!TryParseIds(query.Run, "run", filter.Runs, out error)
            || !TryParseIds(query.Series, "series", filter.Series, out error)
            || !TryParseIds(query.Group, "group", filter.Groups, out error)
            || !TryParseIds(query.Battery, "battery", filter.Batteries, out error)
            || !TryParseIds(query.Batch, "batch", filter.Batches, out error)
            || !TryParseIds(query.Suite, "suite", filter.Suites, out error)
            || !TryParseIds(query.Question, "question", filter.Questions, out error))
        {
            return false;
        }

        string? term = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (term != null && term.Length > MaxSearchLength)
        {
            error = $"The search can be at most {MaxSearchLength} characters.";
            return false;
        }
        filter.Search = term;

        if (string.IsNullOrWhiteSpace(query.Sort)) filter.Sort = SortLastSeen;
        else
        {
            string wanted = query.Sort.Trim();
            string? sort = Sorts.FirstOrDefault(s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase));
            if (sort == null) { error = $"Unknown sort '{query.Sort}'."; return false; }
            filter.Sort = sort;
        }

        error = null;
        return true;
    }

    private static IEnumerable<string> Split(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value);
    }

    private static bool TryParseIds(string? value, string name, List<long> ids, [NotNullWhen(false)] out string? error)
    {
        foreach (var part in Split(value))
        {
            if (!long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out long id))
            {
                error = $"Invalid {name} id '{part}'.";
                return false;
            }
            if (!ids.Contains(id)) ids.Add(id);
        }
        error = null;
        return true;
    }
}

/// <summary>The Overseer chat issue that shares a GnollBench issue's fingerprint.</summary>
public sealed class GnollBenchFeedbackOverseerLink
{
    public long IssueId { get; init; }

    /// <summary>The chat issue's status wire value.</summary>
    public string Status { get; init; } = GnollBenchFeedbackStatuses.New;
}

/// <summary>
/// One GnollBench feedback issue: the self-reports of the base set that share a fingerprint, with its triage state.
/// The aggregates cover every self-report of the base set, not only the ones that matched the occurrence filters.
/// </summary>
public sealed class GnollBenchFeedbackIssue
{
    public string Fingerprint { get; init; } = "";
    public string Category { get; init; } = "";

    /// <summary>From the newest self-report.</summary>
    public string Subject { get; init; } = "";

    /// <summary>From the newest self-report.</summary>
    public string Summary { get; init; } = "";
    public DateTime FirstSeenUtc { get; init; }
    public DateTime LastSeenUtc { get; init; }

    /// <summary>The newest accepted self-report's time; null when the issue has none. The derived status reads it.</summary>
    public DateTime? LastAcceptedUtc { get; init; }
    public int SelfReportCount { get; init; }
    public int RunCount { get; init; }

    /// <summary>Distinct tested model ids, ordered.</summary>
    public IReadOnlyList<string> ModelIds { get; init; } = Array.Empty<string>();

    /// <summary>The display labels of <see cref="ModelIds"/> (<see cref="SystemAiConfigurationSnapshotStore.Label"/>), ordered.</summary>
    public IReadOnlyList<string> ModelLabels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Providers { get; init; } = Array.Empty<string>();

    /// <summary>Distinct suite names, ordered.</summary>
    public IReadOnlyList<string> Suites { get; init; } = Array.Empty<string>();
    public GnollBenchFeedbackStatus Status { get; init; }

    /// <summary>The stored triage status; null when no triage row exists.</summary>
    public BenchmarkFeedbackTriageStatus? StoredStatus { get; init; }
    public string? TriageNote { get; init; }
    public string? Resolution { get; init; }
    public string? FixedInVersion { get; init; }
    public string? TriagedByUserId { get; init; }
    public DateTime? TriagedUtc { get; init; }
    public DateTime? ResolvedUtc { get; init; }
    public GnollBenchFeedbackOverseerLink? AlsoInOverseer { get; init; }

    /// <summary>The newest occurrences, newest first, when they were asked for; empty otherwise.</summary>
    public IReadOnlyList<GnollBenchFeedbackOccurrence> Occurrences { get; init; } = Array.Empty<GnollBenchFeedbackOccurrence>();
}

/// <summary>One self-report with the facts of its answer and run.</summary>
public sealed class GnollBenchFeedbackOccurrence
{
    public long Id { get; init; }
    public string Fingerprint { get; init; } = "";

    /// <summary>The answer's start, else the run's.</summary>
    public DateTime OccurredUtc { get; init; }
    public long RunId { get; init; }
    public long? SeriesId { get; init; }
    public long AnswerId { get; init; }
    public long? SuiteId { get; init; }
    public string SuiteName { get; init; } = "";

    /// <summary>The answer's recorded question id, else its live link; null when neither is known.</summary>
    public long? QuestionId { get; init; }

    /// <summary>The answer's own copy of the question.</summary>
    public string QuestionText { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string ModelLabel { get; init; } = "";
    public string Provider { get; init; } = "";

    /// <summary>The information condition's wire name.</summary>
    public string Condition { get; init; } = BenchmarkInformationConditions.ProductionWire;

    /// <summary>The stored stamp, <c>name:hash12</c>; null for production instructions.</summary>
    public string? Variant { get; init; }

    /// <summary>The variant name without the hash; null for production instructions.</summary>
    public string? VariantName { get; init; }

    /// <summary>The stored corpus condition, or <see cref="GnollBenchFeedbackFilter.LiveCorpus"/>.</summary>
    public string Corpus { get; init; } = GnollBenchFeedbackFilter.LiveCorpus;
    public string? Harness { get; init; }
    public bool Accepted { get; init; }
    public string? Error { get; init; }
    public int SortOrder { get; init; }
    public int? IterationIndex { get; init; }
    public string? Category { get; init; }
    public string? Subject { get; init; }
    public string? Summary { get; init; }
    public string? Details { get; init; }
    public string? SearchedFor { get; init; }
    public string? ExpectedLocation { get; init; }
    public IReadOnlyList<string> RelatedTools { get; init; } = Array.Empty<string>();
    public string? NoticedVia { get; init; }
    public string? SuspectedCause { get; init; }
    public string? SuspectedCauseNote { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>A page of issues and the number that matched.</summary>
public sealed class GnollBenchFeedbackIssuePage
{
    public int TotalCount { get; init; }
    public IReadOnlyList<GnollBenchFeedbackIssue> Rows { get; init; } = Array.Empty<GnollBenchFeedbackIssue>();
}

/// <summary>A page of one issue's occurrences and the number that matched.</summary>
public sealed class GnollBenchFeedbackOccurrencePage
{
    public int TotalCount { get; init; }
    public IReadOnlyList<GnollBenchFeedbackOccurrence> Rows { get; init; } = Array.Empty<GnollBenchFeedbackOccurrence>();
}

/// <summary>The matching fingerprints in sort order, at most the cap, and whether the cap cut them.</summary>
public sealed class GnollBenchFeedbackIdList
{
    public IReadOnlyList<string> Fingerprints { get; init; } = Array.Empty<string>();
    public int TotalCount { get; init; }
    public bool Truncated { get; init; }
}

/// <summary>The triage state of one fingerprint, with the facts a resolution import checks against.</summary>
public sealed class GnollBenchFeedbackTriageState
{
    public string Fingerprint { get; init; } = "";

    /// <summary>At least one self-report, accepted or not, has the fingerprint.</summary>
    public bool Exists { get; init; }
    public GnollBenchFeedbackStatus Status { get; init; }
    public BenchmarkFeedbackTriageStatus? StoredStatus { get; init; }
    public string? TriageNote { get; init; }
    public string? Resolution { get; init; }
    public string? FixedInVersion { get; init; }
    public string? TriagedByUserId { get; init; }
    public DateTime? TriagedUtc { get; init; }
    public DateTime? ResolvedUtc { get; init; }
    public int AcceptedCount { get; init; }
    public DateTime? FirstSeenUtc { get; init; }

    /// <summary>The newest accepted self-report's time.</summary>
    public DateTime? LastSeenUtc { get; init; }
}

/// <summary>
/// A triage change. A null field keeps the stored value and an empty one clears it; <see cref="Status"/> is required.
/// </summary>
public sealed class GnollBenchFeedbackTriageUpdate
{
    public string? Status { get; init; }
    public string? Note { get; init; }
    public string? Resolution { get; init; }
    public string? FixedInVersion { get; init; }
}

public enum GnollBenchFeedbackTriageOutcome
{
    Updated,
    Invalid,
    NotFound,
}

/// <summary>The result of <see cref="GnollBenchFeedbackQuery.SetTriageAsync"/>; <see cref="Message"/> explains a refusal.</summary>
public sealed class GnollBenchFeedbackTriageResult
{
    public GnollBenchFeedbackTriageOutcome Outcome { get; init; }
    public string? Message { get; init; }

    /// <summary>The normalized fingerprint.</summary>
    public string Fingerprint { get; init; } = "";
    public BenchmarkFeedbackTriage? Triage { get; init; }
}

/// <summary>
/// GnollBench feedback issues: <see cref="BenchmarkRunAnswerSelfReport"/> rows grouped by fingerprint at query
/// time, with the triage state kept in <see cref="BenchmarkFeedbackTriage"/>. Shared by the admin API, the Markdown
/// export and the resolution import.
///
/// <para>Grouping, counts and first/last times run in SQL. The triage join, the derived status, the date overlap, the
/// status filter and the sort run in memory over one row per fingerprint. The information condition and the variant
/// name are read in memory over the runs that have self-reports, and the batch filter is resolved to run ids, before
/// the self-report query applies them as a run-id list.</para>
/// </summary>
public sealed class GnollBenchFeedbackQuery
{
    public const int MaxTake = 100;
    public const int MaxIds = 1000;
    public const int MaxListed = 5;
    public const int MaxTriageNoteLength = 1000;
    public const int MaxResolutionLength = 4000;
    public const int MaxFixedInVersionLength = 64;
    public const int MaxQuestionNameLength = 200;

    private static readonly Regex FixedInVersionPattern = new(@"^[0-9A-Za-z.+\-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex FingerprintPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _db;

    public GnollBenchFeedbackQuery(ApplicationDbContext db)
    {
        _db = db;
    }

    // --- Issues ---

    /// <summary>A page of the issues that match <paramref name="filter"/>, in its sort order.</summary>
    public async Task<GnollBenchFeedbackIssuePage> ListIssuesAsync(GnollBenchFeedbackFilter filter, int skip, int take, CancellationToken ct = default)
    {
        var selection = await SelectAsync(filter, ct);
        var sorted = await SortAsync(selection, filter.Sort, ct);
        var page = sorted.Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxTake)).ToList();
        var rows = await MaterializeAsync(selection, page, 0, ct);
        return new GnollBenchFeedbackIssuePage { TotalCount = sorted.Count, Rows = rows };
    }

    /// <summary>Every matching fingerprint in sort order, at most <paramref name="cap"/> (clamped to <see cref="MaxIds"/>).</summary>
    public async Task<GnollBenchFeedbackIdList> ListFingerprintsAsync(GnollBenchFeedbackFilter filter, int cap = MaxIds, CancellationToken ct = default)
    {
        cap = Math.Clamp(cap, 1, MaxIds);
        var selection = await SelectAsync(filter, ct);
        var sorted = await SortAsync(selection, filter.Sort, ct);
        return new GnollBenchFeedbackIdList
        {
            Fingerprints = sorted.Take(cap).Select(c => c.Fingerprint).ToList(),
            TotalCount = sorted.Count,
            Truncated = sorted.Count > cap
        };
    }

    /// <summary>
    /// The issues of <paramref name="fingerprints"/> over the <paramref name="scope"/> base set, ordered by last seen,
    /// each with its <paramref name="newestOccurrences"/> newest occurrences (0 for none). A fingerprint with no
    /// self-report in the base set is left out.
    /// </summary>
    public async Task<List<GnollBenchFeedbackIssue>> LoadIssuesAsync(IReadOnlyCollection<string> fingerprints, int newestOccurrences = 0,
        GnollBenchAcceptedScope scope = GnollBenchAcceptedScope.Accepted, CancellationToken ct = default)
    {
        var normalized = fingerprints.Select(NormalizeFingerprint).Where(f => f.Length > 0).Distinct().ToList();
        if (normalized.Count == 0) return new List<GnollBenchFeedbackIssue>();
        var filter = new GnollBenchFeedbackFilter { Accepted = scope, Fingerprints = normalized };
        var selection = await SelectAsync(filter, ct);
        var sorted = await SortAsync(selection, GnollBenchFeedbackFilter.SortLastSeen, ct);
        return await MaterializeAsync(selection, sorted, Math.Max(0, newestOccurrences), ct);
    }

    /// <summary>Issue counts per derived status over accepted self-reports and all dates.</summary>
    public async Task<Dictionary<GnollBenchFeedbackStatus, int>> CountByStatusAsync(CancellationToken ct = default)
    {
        var selection = await SelectAsync(new GnollBenchFeedbackFilter(), ct);
        var counts = Enum.GetValues<GnollBenchFeedbackStatus>().ToDictionary(s => s, _ => 0);
        foreach (var candidate in selection.Candidates) counts[candidate.Status]++;
        return counts;
    }

    // --- Occurrences ---

    /// <summary>
    /// A page of one issue's self-reports over the filter's base set, newest first. The occurrence-level filters apply
    /// unless <paramref name="all"/> is true; the date range and the status never do. Null when no self-report,
    /// accepted or not, has the fingerprint.
    /// </summary>
    public async Task<GnollBenchFeedbackOccurrencePage?> ListOccurrencesAsync(string fingerprint, GnollBenchFeedbackFilter filter, bool all,
        int skip, int take, CancellationToken ct = default)
    {
        string fp = NormalizeFingerprint(fingerprint);
        if (fp.Length == 0 || !await _db.BenchmarkRunAnswerSelfReports.AnyAsync(s => s.Fingerprint == fp, ct)) return null;

        var query = BaseSet(filter.Accepted).Where(s => s.Fingerprint == fp);
        if (!all)
        {
            var allowed = await ResolveAllowedRunIdsAsync(filter, ct);
            if (allowed != null && allowed.Count == 0) return new GnollBenchFeedbackOccurrencePage();
            query = ApplyOccurrenceFilters(query, filter, allowed);
        }

        int total = await query.CountAsync(ct);
        var rows = await ProjectOccurrences(OrderNewestFirst(query).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxTake)))
            .ToListAsync(ct);
        return new GnollBenchFeedbackOccurrencePage { TotalCount = total, Rows = await CompleteOccurrencesAsync(rows, ct) };
    }

    /// <summary>The accepted self-reports of each fingerprint newer than <paramref name="sinceUtc"/>, counted; absent means none.</summary>
    public async Task<Dictionary<string, int>> CountAcceptedSinceAsync(IReadOnlyCollection<string> fingerprints, DateTime sinceUtc,
        CancellationToken ct = default)
    {
        var fps = fingerprints.Select(NormalizeFingerprint).Where(f => f.Length > 0).Distinct().ToList();
        if (fps.Count == 0) return new Dictionary<string, int>();
        var rows = await BaseSet(GnollBenchAcceptedScope.Accepted)
            .Where(s => fps.Contains(s.Fingerprint!))
            .Where(s => (s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc) > sinceUtc)
            .GroupBy(s => s.Fingerprint!)
            .Select(g => new { Fingerprint = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Fingerprint, r => r.Count, StringComparer.Ordinal);
    }

    /// <summary>The accepted self-reports of one fingerprint newer than <paramref name="sinceUtc"/>, newest first.</summary>
    public async Task<List<GnollBenchFeedbackOccurrence>> ListAcceptedSinceAsync(string fingerprint, DateTime sinceUtc, int take = MaxTake,
        CancellationToken ct = default)
    {
        string fp = NormalizeFingerprint(fingerprint);
        if (fp.Length == 0) return new List<GnollBenchFeedbackOccurrence>();
        var query = BaseSet(GnollBenchAcceptedScope.Accepted)
            .Where(s => s.Fingerprint == fp)
            .Where(s => (s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc) > sinceUtc);
        var rows = await ProjectOccurrences(OrderNewestFirst(query).Take(Math.Clamp(take, 1, MaxTake))).ToListAsync(ct);
        return await CompleteOccurrencesAsync(rows, ct);
    }

    // --- Triage ---

    /// <summary>
    /// The triage state of every requested fingerprint, keyed by its normalized form. A fingerprint no self-report
    /// has reads <see cref="GnollBenchFeedbackTriageState.Exists"/> false.
    /// </summary>
    public async Task<Dictionary<string, GnollBenchFeedbackTriageState>> GetTriageStatesAsync(IReadOnlyCollection<string> fingerprints,
        CancellationToken ct = default)
    {
        var fps = fingerprints.Select(NormalizeFingerprint).Where(f => f.Length > 0).Distinct().ToList();
        var result = new Dictionary<string, GnollBenchFeedbackTriageState>(StringComparer.Ordinal);
        if (fps.Count == 0) return result;

        var existing = (await _db.BenchmarkRunAnswerSelfReports.AsNoTracking()
            .Where(s => s.Fingerprint != null && fps.Contains(s.Fingerprint))
            .Select(s => s.Fingerprint!)
            .Distinct()
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var accepted = await BaseSet(GnollBenchAcceptedScope.Accepted)
            .Where(s => fps.Contains(s.Fingerprint!))
            .Select(s => new
            {
                Fingerprint = s.Fingerprint!,
                Time = s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc
            })
            .GroupBy(x => x.Fingerprint)
            .Select(g => new { Fingerprint = g.Key, Count = g.Count(), First = g.Min(x => x.Time), Last = g.Max(x => x.Time) })
            .ToDictionaryAsync(x => x.Fingerprint, ct);

        var triages = await _db.BenchmarkFeedbackTriages.AsNoTracking()
            .Where(t => fps.Contains(t.Fingerprint))
            .ToDictionaryAsync(t => t.Fingerprint, ct);

        foreach (var fp in fps)
        {
            triages.TryGetValue(fp, out var triage);
            bool hasAccepted = accepted.TryGetValue(fp, out var stats);
            DateTime? last = hasAccepted ? stats!.Last : null;
            result[fp] = new GnollBenchFeedbackTriageState
            {
                Fingerprint = fp,
                Exists = existing.Contains(fp),
                Status = GnollBenchFeedbackStatuses.Derive(triage, last),
                StoredStatus = triage?.Status,
                TriageNote = triage?.TriageNote,
                Resolution = triage?.Resolution,
                FixedInVersion = triage?.FixedInVersion,
                TriagedByUserId = triage?.TriagedByUserId,
                TriagedUtc = triage?.TriagedUtc,
                ResolvedUtc = triage?.ResolvedUtc,
                AcceptedCount = hasAccepted ? stats!.Count : 0,
                FirstSeenUtc = hasAccepted ? stats!.First : null,
                LastSeenUtc = last
            };
        }
        return result;
    }

    /// <summary>
    /// Creates or updates the triage row of <paramref name="fingerprint"/> and saves it. <c>regressed</c> is refused,
    /// because it is derived. A null note, resolution or version keeps the stored value and an empty one clears it.
    /// Becoming Resolved (from any other shown status, Regressed included) sets <see cref="BenchmarkFeedbackTriage.ResolvedUtc"/>
    /// to now, staying Resolved keeps it, and any other status clears it. NotFound when no self-report has the fingerprint.
    /// </summary>
    public async Task<GnollBenchFeedbackTriageResult> SetTriageAsync(string fingerprint, GnollBenchFeedbackTriageUpdate update, string? userId,
        CancellationToken ct = default)
    {
        string fp = NormalizeFingerprint(fingerprint);
        if (!GnollBenchFeedbackStatuses.TryParse(update.Status, out var status))
            return Invalid(fp, $"Unknown status '{update.Status}'.");
        if (status == GnollBenchFeedbackStatus.Regressed)
            return Invalid(fp, "Regressed is shown when a resolved issue is reported again; it cannot be chosen.");

        if (!TryNormalizeText(update.Note, MaxTriageNoteLength, out string? note, out bool clearNote))
            return Invalid(fp, $"The note can be at most {MaxTriageNoteLength} characters.");
        if (!TryNormalizeText(update.Resolution, MaxResolutionLength, out string? resolution, out bool clearResolution))
            return Invalid(fp, $"The resolution can be at most {MaxResolutionLength} characters.");
        if (!TryNormalizeText(update.FixedInVersion, MaxFixedInVersionLength, out string? version, out bool clearVersion)
            || (version != null && !FixedInVersionPattern.IsMatch(version)))
            return Invalid(fp, "The fixed-in version must be 1 to 64 letters, digits, '.', '+' or '-'.");

        if (fp.Length == 0 || !await _db.BenchmarkRunAnswerSelfReports.AnyAsync(s => s.Fingerprint == fp, ct))
            return new GnollBenchFeedbackTriageResult { Outcome = GnollBenchFeedbackTriageOutcome.NotFound, Fingerprint = fp, Message = "No such GnollBench feedback issue." };

        DateTime? lastAccepted = await LastAcceptedAsync(fp, ct);
        var triage = await _db.BenchmarkFeedbackTriages.FirstOrDefaultAsync(t => t.Fingerprint == fp, ct);
        bool created = triage == null;
        if (triage == null)
        {
            triage = new BenchmarkFeedbackTriage { Fingerprint = fp };
            _db.BenchmarkFeedbackTriages.Add(triage);
        }

        var shownBefore = created ? GnollBenchFeedbackStatus.New : GnollBenchFeedbackStatuses.Derive(triage, lastAccepted);
        DateTime now = DateTime.UtcNow;

        triage.Status = status switch
        {
            GnollBenchFeedbackStatus.Acknowledged => BenchmarkFeedbackTriageStatus.Acknowledged,
            GnollBenchFeedbackStatus.Resolved => BenchmarkFeedbackTriageStatus.Resolved,
            GnollBenchFeedbackStatus.Dismissed => BenchmarkFeedbackTriageStatus.Dismissed,
            _ => BenchmarkFeedbackTriageStatus.New
        };
        if (status == GnollBenchFeedbackStatus.Resolved)
        {
            if (shownBefore != GnollBenchFeedbackStatus.Resolved || triage.ResolvedUtc == null) triage.ResolvedUtc = now;
        }
        else
        {
            triage.ResolvedUtc = null;
        }

        if (clearNote) triage.TriageNote = null; else if (note != null) triage.TriageNote = note;
        if (clearResolution) triage.Resolution = null; else if (resolution != null) triage.Resolution = resolution;
        if (clearVersion) triage.FixedInVersion = null; else if (version != null) triage.FixedInVersion = version;
        triage.TriagedByUserId = userId;
        triage.TriagedUtc = now;

        await _db.SaveChangesAsync(ct);
        return new GnollBenchFeedbackTriageResult { Outcome = GnollBenchFeedbackTriageOutcome.Updated, Fingerprint = fp, Triage = triage };
    }

    // --- Facets ---

    /// <summary>
    /// The filter options present among the <paramref name="scope"/> self-reports whose time falls in the range, each
    /// with the number of distinct issues it covers. Only runs that have self-reports appear.
    /// </summary>
    public async Task<GnollBenchFeedbackFacetsDto> GetFacetsAsync(DateTime? startDate, DateTime? endDate,
        GnollBenchAcceptedScope scope = GnollBenchAcceptedScope.Accepted, CancellationToken ct = default)
    {
        var query = BaseSet(scope);
        if (startDate.HasValue)
        {
            var from = startDate.Value;
            query = query.Where(s => (s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc) >= from);
        }
        if (endDate.HasValue)
        {
            var to = endDate.Value.AddDays(1);
            query = query.Where(s => (s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc) < to);
        }

        var rows = await query
            .Select(s => new
            {
                Fingerprint = s.Fingerprint!,
                s.Category,
                AnswerId = s.BenchmarkRunAnswerId,
                RunId = s.BenchmarkRunAnswer!.BenchmarkRunId,
                QuestionId = s.BenchmarkRunAnswer.BenchmarkQuestionIdUsed ?? s.BenchmarkRunAnswer.BenchmarkQuestionId
            })
            .Distinct()
            .ToListAsync(ct);

        var dto = new GnollBenchFeedbackFacetsDto();
        if (rows.Count == 0) return dto;

        var runIds = rows.Select(r => r.RunId).Distinct().ToList();
        var runs = await LoadRunInfoAsync(runIds, ct);
        var fpsByRun = rows.GroupBy(r => r.RunId).ToDictionary(g => g.Key, g => g.Select(r => r.Fingerprint).ToHashSet(StringComparer.Ordinal));

        HashSet<string> FingerprintsOfRuns(IEnumerable<long> ids)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
                if (fpsByRun.TryGetValue(id, out var fps)) set.UnionWith(fps);
            return set;
        }

        // Categories, in the tool's own order.
        var byCategory = rows.Where(r => r.Category != null).GroupBy(r => r.Category!)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Fingerprint).Distinct().Count(), StringComparer.Ordinal);
        dto.Categories = FeedbackCategories.All.Where(byCategory.ContainsKey)
            .Concat(byCategory.Keys.Where(k => !FeedbackCategories.All.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
            .Select(c => new GnollBenchFeedbackFacetValueDto { Value = c, Label = c, Count = byCategory[c] })
            .ToList();

        // Run-level values.
        var runList = runs.Values.ToList();
        dto.Models = runList.GroupBy(r => r.ModelId, StringComparer.Ordinal)
            .Select(g => new GnollBenchFeedbackFacetValueDto
            {
                Value = g.Key,
                Label = g.Select(r => r.ModelLabel).FirstOrDefault(l => l != g.Key) ?? g.Key,
                Count = FingerprintsOfRuns(g.Select(r => r.Id)).Count
            })
            .OrderBy(v => v.Label, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.Value, StringComparer.Ordinal)
            .ToList();
        dto.Providers = ValueFacet(runList.GroupBy(r => r.Provider, StringComparer.Ordinal), FingerprintsOfRuns)
            .OrderBy(v => v.Value, StringComparer.OrdinalIgnoreCase).ToList();
        dto.Runs = runList
            .Select(r => new GnollBenchFeedbackFacetEntityDto
            {
                Id = r.Id,
                Name = $"Run #{r.Id} · {r.SuiteName} · {r.ModelLabel}",
                Count = FingerprintsOfRuns(new[] { r.Id }).Count
            })
            .OrderByDescending(e => e.Id)
            .ToList();
        dto.Suites = runList.Where(r => r.SuiteId.HasValue).GroupBy(r => r.SuiteId!.Value)
            .Select(g => new GnollBenchFeedbackFacetEntityDto
            {
                Id = g.Key,
                Name = g.OrderByDescending(r => r.Id).First().SuiteName,
                Count = FingerprintsOfRuns(g.Select(r => r.Id)).Count
            })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id)
            .ToList();
        dto.Conditions = runList.GroupBy(r => r.Condition)
            .OrderBy(g => (int)g.Key)
            .Select(g => new GnollBenchFeedbackFacetValueDto
            {
                Value = g.Key.ToWire(),
                Label = ConditionLabel(g.Key),
                Count = FingerprintsOfRuns(g.Select(r => r.Id)).Count
            })
            .ToList();
        dto.Corpora = ValueFacet(runList.GroupBy(r => r.Corpus, StringComparer.Ordinal), FingerprintsOfRuns)
            .OrderBy(v => v.Value == GnollBenchFeedbackFilter.LiveCorpus ? 0 : 1).ThenBy(v => v.Value, StringComparer.Ordinal)
            .ToList();
        dto.Variants = ValueFacet(runList.GroupBy(r => r.VariantName ?? GnollBenchFeedbackFilter.NoVariant, StringComparer.Ordinal), FingerprintsOfRuns)
            .OrderBy(v => v.Value == GnollBenchFeedbackFilter.NoVariant ? 0 : 1).ThenBy(v => v.Value, StringComparer.Ordinal)
            .ToList();
        dto.Harnesses = ValueFacet(runList.Where(r => r.Harness != null).GroupBy(r => r.Harness!, StringComparer.Ordinal), FingerprintsOfRuns)
            .OrderBy(v => int.TryParse(v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? 0 : 1)
            .ThenBy(v => int.TryParse(v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .ThenBy(v => v.Value, StringComparer.Ordinal)
            .ToList();

        // Series.
        var seriesRuns = runList.Where(r => r.SeriesId.HasValue).GroupBy(r => r.SeriesId!.Value).ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());
        if (seriesRuns.Count > 0)
        {
            var seriesIds = seriesRuns.Keys.ToList();
            var seriesNames = await _db.BenchmarkRunSeries.AsNoTracking()
                .Where(s => seriesIds.Contains(s.Id))
                .Select(s => new { s.Id, s.SuiteName })
                .ToDictionaryAsync(s => s.Id, s => s.SuiteName, ct);
            dto.Series = seriesRuns
                .Select(kv => new GnollBenchFeedbackFacetEntityDto
                {
                    Id = kv.Key,
                    Name = seriesNames.TryGetValue(kv.Key, out var name) ? $"Series #{kv.Key} · {name}" : $"Series #{kv.Key}",
                    Count = FingerprintsOfRuns(kv.Value).Count
                })
                .OrderByDescending(e => e.Id)
                .ToList();
        }

        // Analysis groups.
        var groupMembers = await _db.BenchmarkRunGroupMembers.AsNoTracking()
            .Where(m => runIds.Contains(m.BenchmarkRunId))
            .Select(m => new { m.BenchmarkRunGroupId, m.BenchmarkRunId, m.BenchmarkRunGroup.Name })
            .ToListAsync(ct);
        dto.Groups = groupMembers.GroupBy(m => m.BenchmarkRunGroupId)
            .Select(g => new GnollBenchFeedbackFacetEntityDto
            {
                Id = g.Key,
                Name = g.First().Name,
                Count = FingerprintsOfRuns(g.Select(m => m.BenchmarkRunId)).Count
            })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id)
            .ToList();

        // Battery runs.
        var batteryMembers = await _db.BenchmarkBatteryRunMembers.AsNoTracking()
            .Where(m => runIds.Contains(m.BenchmarkRunId))
            .Select(m => new { m.BenchmarkBatteryRunId, m.BenchmarkRunId, m.BenchmarkBatteryRun.BatteryName })
            .ToListAsync(ct);
        var batteryRuns = batteryMembers.GroupBy(m => m.BenchmarkBatteryRunId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.BenchmarkRunId).Distinct().ToList());
        dto.Batteries = batteryMembers.GroupBy(m => m.BenchmarkBatteryRunId)
            .Select(g => new GnollBenchFeedbackFacetEntityDto
            {
                Id = g.Key,
                Name = $"{g.First().BatteryName} #{g.Key}",
                Count = FingerprintsOfRuns(batteryRuns[g.Key]).Count
            })
            .OrderByDescending(e => e.Id)
            .ToList();

        // Model batches: a member names a run, a series or a battery run.
        var seriesKeys = seriesRuns.Keys.ToList();
        var batteryKeys = batteryRuns.Keys.ToList();
        var batchMembers = await _db.BenchmarkModelBatchMembers.AsNoTracking()
            .Where(b => (b.BenchmarkRunId != null && runIds.Contains(b.BenchmarkRunId.Value))
                || (b.BenchmarkRunSeriesId != null && seriesKeys.Contains(b.BenchmarkRunSeriesId.Value))
                || (b.BenchmarkBatteryRunId != null && batteryKeys.Contains(b.BenchmarkBatteryRunId.Value)))
            .Select(b => new
            {
                b.BenchmarkModelBatchRunId,
                b.BenchmarkRunId,
                b.BenchmarkRunSeriesId,
                b.BenchmarkBatteryRunId,
                b.BenchmarkModelBatchRun.TargetName
            })
            .ToListAsync(ct);
        dto.Batches = batchMembers.GroupBy(b => b.BenchmarkModelBatchRunId)
            .Select(g =>
            {
                var covered = new List<long>();
                foreach (var member in g)
                {
                    if (member.BenchmarkRunId.HasValue) covered.Add(member.BenchmarkRunId.Value);
                    if (member.BenchmarkRunSeriesId.HasValue && seriesRuns.TryGetValue(member.BenchmarkRunSeriesId.Value, out var sr)) covered.AddRange(sr);
                    if (member.BenchmarkBatteryRunId.HasValue && batteryRuns.TryGetValue(member.BenchmarkBatteryRunId.Value, out var br)) covered.AddRange(br);
                }
                string? target = g.Select(b => b.TargetName).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                return new GnollBenchFeedbackFacetEntityDto
                {
                    Id = g.Key,
                    Name = target != null ? $"Batch #{g.Key} · {target}" : $"Batch #{g.Key}",
                    Count = FingerprintsOfRuns(covered).Count
                };
            })
            .Where(e => e.Count > 0)
            .OrderByDescending(e => e.Id)
            .ToList();

        // Questions, named by the newest answer's own copy of the text.
        var questionRows = rows.Where(r => r.QuestionId.HasValue).ToList();
        if (questionRows.Count > 0)
        {
            var newestAnswerByQuestion = questionRows.GroupBy(r => r.QuestionId!.Value).ToDictionary(g => g.Key, g => g.Max(r => r.AnswerId));
            var answerIds = newestAnswerByQuestion.Values.Distinct().ToList();
            var texts = await _db.BenchmarkRunAnswers.AsNoTracking()
                .Where(a => answerIds.Contains(a.Id))
                .Select(a => new { a.Id, a.QuestionText })
                .ToDictionaryAsync(a => a.Id, a => a.QuestionText, ct);
            dto.Questions = questionRows.GroupBy(r => r.QuestionId!.Value)
                .Select(g => new GnollBenchFeedbackFacetEntityDto
                {
                    Id = g.Key,
                    Name = Shorten(texts.GetValueOrDefault(newestAnswerByQuestion[g.Key]) ?? $"Question #{g.Key}", MaxQuestionNameLength),
                    Count = g.Select(r => r.Fingerprint).Distinct().Count()
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id)
                .ToList();
        }

        return dto;
    }

    // --- Selection pipeline ---

    private sealed class Candidate
    {
        public string Fingerprint { get; init; } = "";
        public int Count { get; init; }
        public DateTime FirstSeenUtc { get; init; }
        public DateTime LastSeenUtc { get; init; }
        public long LatestId { get; init; }
        public DateTime? LastAcceptedUtc { get; init; }
        public BenchmarkFeedbackTriage? Triage { get; init; }
        public GnollBenchFeedbackStatus Status { get; init; }

        // Filled by EnrichRunsAsync and LoadLatestAsync.
        public HashSet<long>? RunIds { get; set; }
        public string? Category { get; set; }
        public string? Subject { get; set; }
        public string? Summary { get; set; }
    }

    private sealed class Selection
    {
        /// <summary>The base set restricted to the selected fingerprints, before the status and date filters.</summary>
        public IQueryable<BenchmarkRunAnswerSelfReport> Scoped { get; init; } = default!;
        public List<Candidate> Candidates { get; init; } = new();
        public Dictionary<long, RunInfo> Runs { get; } = new();
    }

    private sealed class RunInfo
    {
        public long Id { get; init; }
        public long? SeriesId { get; init; }
        public long? SuiteId { get; init; }
        public string SuiteName { get; init; } = "";
        public string ModelId { get; init; } = "";
        public string ModelLabel { get; init; } = "";
        public string Provider { get; init; } = "";
        public BenchmarkInformationCondition Condition { get; init; }
        public string? Variant { get; init; }
        public string? VariantName { get; init; }
        public string Corpus { get; init; } = GnollBenchFeedbackFilter.LiveCorpus;
        public string? Harness { get; init; }
    }

    private sealed class OccurrenceRow
    {
        public long Id { get; init; }
        public string Fingerprint { get; init; } = "";
        public DateTime OccurredUtc { get; init; }
        public long RunId { get; init; }
        public long AnswerId { get; init; }
        public long? QuestionId { get; init; }
        public string QuestionText { get; init; } = "";
        public bool Accepted { get; init; }
        public string? Error { get; init; }
        public int SortOrder { get; init; }
        public int? IterationIndex { get; init; }
        public string? Category { get; init; }
        public string? Subject { get; init; }
        public string? Summary { get; init; }
        public string? Details { get; init; }
        public string? SearchedFor { get; init; }
        public string? ExpectedLocation { get; init; }
        public string? RelatedToolsJson { get; init; }
        public string? NoticedVia { get; init; }
        public string? SuspectedCause { get; init; }
        public string? SuspectedCauseNote { get; init; }
        public bool Truncated { get; init; }
    }

    /// <summary>
    /// The fingerprints the filter selects, with their counts and times over the base set, their triage rows and
    /// derived status, after the date overlap and the status filter. Unsorted.
    /// </summary>
    private async Task<Selection> SelectAsync(GnollBenchFeedbackFilter filter, CancellationToken ct)
    {
        var baseSet = BaseSet(filter.Accepted);
        var allowed = await ResolveAllowedRunIdsAsync(filter, ct);
        if (allowed != null && allowed.Count == 0)
            return new Selection { Scoped = baseSet.Where(_ => false) };

        IQueryable<string> fingerprints;
        IQueryable<BenchmarkRunAnswerSelfReport> scoped;
        if (filter.HasOccurrenceFilters || allowed != null)
        {
            fingerprints = ApplyOccurrenceFilters(baseSet, filter, allowed).Select(s => s.Fingerprint!).Distinct();
            scoped = baseSet.Where(s => fingerprints.Contains(s.Fingerprint!));
        }
        else
        {
            fingerprints = baseSet.Select(s => s.Fingerprint!).Distinct();
            scoped = baseSet;
        }

        var aggregates = await scoped
            .Select(s => new
            {
                Fingerprint = s.Fingerprint!,
                s.Id,
                Time = s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc
            })
            .GroupBy(x => x.Fingerprint)
            .Select(g => new
            {
                Fingerprint = g.Key,
                Count = g.Count(),
                First = g.Min(x => x.Time),
                Last = g.Max(x => x.Time),
                LatestId = g.Max(x => x.Id)
            })
            .ToListAsync(ct);
        if (aggregates.Count == 0) return new Selection { Scoped = scoped };

        var triages = await _db.BenchmarkFeedbackTriages.AsNoTracking()
            .Where(t => fingerprints.Contains(t.Fingerprint))
            .ToDictionaryAsync(t => t.Fingerprint, StringComparer.Ordinal, ct);

        Dictionary<string, DateTime> lastAccepted;
        if (filter.Accepted == GnollBenchAcceptedScope.Accepted)
        {
            lastAccepted = aggregates.ToDictionary(a => a.Fingerprint, a => a.Last, StringComparer.Ordinal);
        }
        else
        {
            var accepted = await _db.BenchmarkRunAnswerSelfReports.AsNoTracking()
                .Where(s => s.Accepted && s.Fingerprint != null && fingerprints.Contains(s.Fingerprint))
                .Select(s => new
                {
                    Fingerprint = s.Fingerprint!,
                    Time = s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc
                })
                .GroupBy(x => x.Fingerprint)
                .Select(g => new { Fingerprint = g.Key, Last = g.Max(x => x.Time) })
                .ToListAsync(ct);
            lastAccepted = accepted.ToDictionary(a => a.Fingerprint, a => a.Last, StringComparer.Ordinal);
        }

        DateTime? from = filter.StartDate;
        DateTime? to = filter.EndDate?.AddDays(1);
        var candidates = new List<Candidate>(aggregates.Count);
        foreach (var a in aggregates)
        {
            if (from.HasValue && a.Last < from.Value) continue;
            if (to.HasValue && a.First >= to.Value) continue;
            triages.TryGetValue(a.Fingerprint, out var triage);
            DateTime? last = lastAccepted.TryGetValue(a.Fingerprint, out var l) ? l : null;
            var status = GnollBenchFeedbackStatuses.Derive(triage, last);
            if (filter.Statuses.Count > 0 && !filter.Statuses.Contains(status)) continue;
            candidates.Add(new Candidate
            {
                Fingerprint = a.Fingerprint,
                Count = a.Count,
                FirstSeenUtc = a.First,
                LastSeenUtc = a.Last,
                LatestId = a.LatestId,
                LastAcceptedUtc = last,
                Triage = triage,
                Status = status
            });
        }
        return new Selection { Scoped = scoped, Candidates = candidates };
    }

    /// <summary>The candidates in <paramref name="sort"/> order, loading the run and subject facts that order needs.</summary>
    private async Task<List<Candidate>> SortAsync(Selection selection, string sort, CancellationToken ct)
    {
        var candidates = selection.Candidates;
        if (sort is GnollBenchFeedbackFilter.SortRuns or GnollBenchFeedbackFilter.SortModels)
            await EnrichRunsAsync(selection, candidates, ct);
        if (sort == GnollBenchFeedbackFilter.SortSubject)
            await LoadLatestAsync(candidates, ct);

        IOrderedEnumerable<Candidate> ordered = sort switch
        {
            GnollBenchFeedbackFilter.SortFirstSeen => candidates.OrderByDescending(c => c.FirstSeenUtc),
            GnollBenchFeedbackFilter.SortCount => candidates.OrderByDescending(c => c.Count).ThenByDescending(c => c.LastSeenUtc),
            GnollBenchFeedbackFilter.SortRuns => candidates.OrderByDescending(c => c.RunIds?.Count ?? 0).ThenByDescending(c => c.LastSeenUtc),
            GnollBenchFeedbackFilter.SortModels => candidates.OrderByDescending(c => ModelIdsOf(selection, c).Count).ThenByDescending(c => c.LastSeenUtc),
            GnollBenchFeedbackFilter.SortSubject => candidates.OrderBy(c => c.Subject ?? "", StringComparer.OrdinalIgnoreCase).ThenByDescending(c => c.LastSeenUtc),
            _ => candidates.OrderByDescending(c => c.LastSeenUtc)
        };
        return ordered.ThenBy(c => c.Fingerprint, StringComparer.Ordinal).ToList();
    }

    /// <summary>Builds the issues of <paramref name="page"/>, in its order, with up to <paramref name="newestOccurrences"/> occurrences each.</summary>
    private async Task<List<GnollBenchFeedbackIssue>> MaterializeAsync(Selection selection, List<Candidate> page, int newestOccurrences,
        CancellationToken ct)
    {
        if (page.Count == 0) return new List<GnollBenchFeedbackIssue>();
        await EnrichRunsAsync(selection, page, ct);
        await LoadLatestAsync(page, ct);

        var fps = page.Select(c => c.Fingerprint).ToList();
        var overseer = await _db.OverseerFeedbackIssues.AsNoTracking()
            .Where(i => fps.Contains(i.Fingerprint))
            .Select(i => new { i.Id, i.Fingerprint, i.Status })
            .ToDictionaryAsync(i => i.Fingerprint, StringComparer.Ordinal, ct);

        var occurrences = new Dictionary<string, List<GnollBenchFeedbackOccurrence>>(StringComparer.Ordinal);
        if (newestOccurrences > 0)
        {
            var light = await selection.Scoped
                .Where(s => fps.Contains(s.Fingerprint!))
                .Select(s => new
                {
                    Fingerprint = s.Fingerprint!,
                    s.Id,
                    Time = s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc
                })
                .ToListAsync(ct);
            var ids = light.GroupBy(x => x.Fingerprint)
                .SelectMany(g => g.OrderByDescending(x => x.Time).ThenByDescending(x => x.Id).Take(newestOccurrences).Select(x => x.Id))
                .ToList();
            var rows = await ProjectOccurrences(_db.BenchmarkRunAnswerSelfReports.AsNoTracking().Where(s => ids.Contains(s.Id))).ToListAsync(ct);
            foreach (var occurrence in await CompleteOccurrencesAsync(rows, ct))
            {
                if (!occurrences.TryGetValue(occurrence.Fingerprint, out var list)) occurrences[occurrence.Fingerprint] = list = new();
                list.Add(occurrence);
            }
            foreach (var list in occurrences.Values)
                list.Sort((a, b) => b.OccurredUtc != a.OccurredUtc ? b.OccurredUtc.CompareTo(a.OccurredUtc) : b.Id.CompareTo(a.Id));
        }

        var issues = new List<GnollBenchFeedbackIssue>(page.Count);
        foreach (var c in page)
        {
            var runs = (c.RunIds ?? new HashSet<long>()).Select(id => selection.Runs.GetValueOrDefault(id)).Where(r => r != null).Select(r => r!).ToList();
            var models = runs.GroupBy(r => r.ModelId, StringComparer.Ordinal)
                .Select(g => (Id: g.Key, Label: g.Select(r => r.ModelLabel).FirstOrDefault(l => l != g.Key) ?? g.Key))
                .OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Id, StringComparer.Ordinal)
                .ToList();
            overseer.TryGetValue(c.Fingerprint, out var link);
            issues.Add(new GnollBenchFeedbackIssue
            {
                Fingerprint = c.Fingerprint,
                Category = c.Category ?? "",
                Subject = c.Subject ?? "",
                Summary = c.Summary ?? "",
                FirstSeenUtc = c.FirstSeenUtc,
                LastSeenUtc = c.LastSeenUtc,
                LastAcceptedUtc = c.LastAcceptedUtc,
                SelfReportCount = c.Count,
                RunCount = c.RunIds?.Count ?? 0,
                ModelIds = models.Select(m => m.Id).ToList(),
                ModelLabels = models.Select(m => m.Label).ToList(),
                Providers = runs.Select(r => r.Provider).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
                Suites = runs.Select(r => r.SuiteName).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                Status = c.Status,
                StoredStatus = c.Triage?.Status,
                TriageNote = c.Triage?.TriageNote,
                Resolution = c.Triage?.Resolution,
                FixedInVersion = c.Triage?.FixedInVersion,
                TriagedByUserId = c.Triage?.TriagedByUserId,
                TriagedUtc = c.Triage?.TriagedUtc,
                ResolvedUtc = c.Triage?.ResolvedUtc,
                AlsoInOverseer = link == null ? null : new GnollBenchFeedbackOverseerLink { IssueId = link.Id, Status = OverseerStatusWire(link.Status) },
                Occurrences = occurrences.TryGetValue(c.Fingerprint, out var own) ? own : Array.Empty<GnollBenchFeedbackOccurrence>()
            });
        }
        return issues;
    }

    /// <summary>Fills each candidate's run ids, and the selection's run facts, from the distinct fingerprint and run pairs.</summary>
    private async Task EnrichRunsAsync(Selection selection, List<Candidate> candidates, CancellationToken ct)
    {
        var missing = candidates.Where(c => c.RunIds == null).ToList();
        if (missing.Count == 0) return;

        var query = selection.Scoped;
        if (missing.Count < selection.Candidates.Count)
        {
            var fps = missing.Select(c => c.Fingerprint).ToList();
            query = query.Where(s => fps.Contains(s.Fingerprint!));
        }
        var pairs = await query
            .Select(s => new { Fingerprint = s.Fingerprint!, RunId = s.BenchmarkRunAnswer!.BenchmarkRunId })
            .Distinct()
            .ToListAsync(ct);
        var byFingerprint = pairs.GroupBy(p => p.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.RunId).ToHashSet(), StringComparer.Ordinal);
        foreach (var c in missing) c.RunIds = byFingerprint.GetValueOrDefault(c.Fingerprint) ?? new HashSet<long>();

        var unknown = pairs.Select(p => p.RunId).Distinct().Where(id => !selection.Runs.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            foreach (var (id, info) in await LoadRunInfoAsync(unknown, ct)) selection.Runs[id] = info;
    }

    /// <summary>Fills each candidate's category, subject and summary from its newest self-report.</summary>
    private async Task LoadLatestAsync(List<Candidate> candidates, CancellationToken ct)
    {
        var missing = candidates.Where(c => c.Subject == null).ToList();
        if (missing.Count == 0) return;
        var ids = missing.Select(c => c.LatestId).ToList();
        var latest = await _db.BenchmarkRunAnswerSelfReports.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Category, s.Subject, s.Summary })
            .ToDictionaryAsync(s => s.Id, ct);
        foreach (var c in missing)
        {
            latest.TryGetValue(c.LatestId, out var row);
            c.Category = row?.Category ?? "";
            c.Subject = row?.Subject ?? "";
            c.Summary = row?.Summary ?? "";
        }
    }

    private static List<string> ModelIdsOf(Selection selection, Candidate c)
        => (c.RunIds ?? new HashSet<long>())
            .Select(id => selection.Runs.GetValueOrDefault(id)?.ModelId)
            .Where(m => m != null)
            .Select(m => m!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // --- Query building ---

    private IQueryable<BenchmarkRunAnswerSelfReport> BaseSet(GnollBenchAcceptedScope scope)
    {
        var query = _db.BenchmarkRunAnswerSelfReports.AsNoTracking().Where(s => s.Fingerprint != null);
        return scope switch
        {
            GnollBenchAcceptedScope.All => query,
            GnollBenchAcceptedScope.Refused => query.Where(s => !s.Accepted),
            _ => query.Where(s => s.Accepted)
        };
    }

    private static IQueryable<BenchmarkRunAnswerSelfReport> OrderNewestFirst(IQueryable<BenchmarkRunAnswerSelfReport> query)
        => query
            .OrderByDescending(s => s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc)
            .ThenByDescending(s => s.Id);

    /// <summary>
    /// The occurrence-level filters that translate to SQL. The batch, condition and variant filters arrive resolved as
    /// <paramref name="allowedRunIds"/>, together with the run filter; null means no run restriction.
    /// </summary>
    private IQueryable<BenchmarkRunAnswerSelfReport> ApplyOccurrenceFilters(IQueryable<BenchmarkRunAnswerSelfReport> query,
        GnollBenchFeedbackFilter filter, HashSet<long>? allowedRunIds)
    {
        if (allowedRunIds != null)
        {
            var ids = allowedRunIds.ToList();
            query = query.Where(s => ids.Contains(s.BenchmarkRunAnswer!.BenchmarkRunId));
        }
        if (filter.Categories.Count > 0)
        {
            var categories = filter.Categories;
            query = query.Where(s => s.Category != null && categories.Contains(s.Category));
        }
        if (filter.Models.Count > 0)
        {
            var models = filter.Models;
            query = query.Where(s => models.Contains(s.BenchmarkRunAnswer!.BenchmarkRun.TestedModelSnapshot.ModelId));
        }
        if (filter.Providers.Count > 0)
        {
            var providers = filter.Providers;
            query = query.Where(s => providers.Contains(s.BenchmarkRunAnswer!.BenchmarkRun.TestedModelSnapshot.Provider));
        }
        if (filter.Series.Count > 0)
        {
            var series = filter.Series;
            query = query.Where(s => s.BenchmarkRunAnswer!.BenchmarkRun.RunSeriesId != null
                && series.Contains(s.BenchmarkRunAnswer.BenchmarkRun.RunSeriesId.Value));
        }
        if (filter.Groups.Count > 0)
        {
            var groups = filter.Groups;
            query = query.Where(s => _db.BenchmarkRunGroupMembers.Any(m =>
                m.BenchmarkRunId == s.BenchmarkRunAnswer!.BenchmarkRunId && groups.Contains(m.BenchmarkRunGroupId)));
        }
        if (filter.Batteries.Count > 0)
        {
            var batteries = filter.Batteries;
            query = query.Where(s => _db.BenchmarkBatteryRunMembers.Any(m =>
                m.BenchmarkRunId == s.BenchmarkRunAnswer!.BenchmarkRunId && batteries.Contains(m.BenchmarkBatteryRunId)));
        }
        if (filter.Suites.Count > 0)
        {
            var suites = filter.Suites;
            query = query.Where(s =>
                (s.BenchmarkRunAnswer!.BenchmarkRun.BenchmarkSuiteIdUsed != null
                    && suites.Contains(s.BenchmarkRunAnswer.BenchmarkRun.BenchmarkSuiteIdUsed.Value))
                || (s.BenchmarkRunAnswer.BenchmarkRun.BenchmarkSuiteIdUsed == null
                    && s.BenchmarkRunAnswer.BenchmarkRun.BenchmarkSuiteId != null
                    && suites.Contains(s.BenchmarkRunAnswer.BenchmarkRun.BenchmarkSuiteId.Value)));
        }
        if (filter.Questions.Count > 0)
        {
            var questions = filter.Questions;
            query = query.Where(s =>
                (s.BenchmarkRunAnswer!.BenchmarkQuestionIdUsed != null && questions.Contains(s.BenchmarkRunAnswer.BenchmarkQuestionIdUsed.Value))
                || (s.BenchmarkRunAnswer.BenchmarkQuestionIdUsed == null
                    && s.BenchmarkRunAnswer.BenchmarkQuestionId != null
                    && questions.Contains(s.BenchmarkRunAnswer.BenchmarkQuestionId.Value)));
        }
        if (filter.Corpora.Count > 0)
        {
            bool live = filter.Corpora.Contains(GnollBenchFeedbackFilter.LiveCorpus, StringComparer.Ordinal);
            var named = filter.Corpora.Where(c => c != GnollBenchFeedbackFilter.LiveCorpus).ToList();
            query = query.Where(s =>
                (live && s.BenchmarkRunAnswer!.BenchmarkRun.CorpusCondition == null)
                || (s.BenchmarkRunAnswer!.BenchmarkRun.CorpusCondition != null && named.Contains(s.BenchmarkRunAnswer.BenchmarkRun.CorpusCondition)));
        }
        if (filter.Harnesses.Count > 0)
        {
            var harnesses = filter.Harnesses;
            query = query.Where(s => s.BenchmarkRunAnswer!.BenchmarkRun.HarnessVersion != null
                && harnesses.Contains(s.BenchmarkRunAnswer.BenchmarkRun.HarnessVersion));
        }
        if (filter.Fingerprints.Count > 0)
        {
            var fps = filter.Fingerprints;
            query = query.Where(s => fps.Contains(s.Fingerprint!));
        }
        if (filter.Search != null)
        {
            // Lowered on both sides so the match ignores case on every provider, whatever the collation.
            var lowered = filter.Search.ToLowerInvariant();
            query = query.Where(s =>
                (s.Subject != null && s.Subject.ToLower().Contains(lowered))
                || (s.Summary != null && s.Summary.ToLower().Contains(lowered))
                || (s.Details != null && s.Details.ToLower().Contains(lowered))
                || s.BenchmarkRunAnswer!.QuestionText.ToLower().Contains(lowered));
        }
        return query;
    }

    /// <summary>
    /// The intersection of the run, batch, condition and variant filters as run ids; null when none is set. The
    /// condition and variant are read in memory over the runs that have self-reports.
    /// </summary>
    private async Task<HashSet<long>?> ResolveAllowedRunIdsAsync(GnollBenchFeedbackFilter filter, CancellationToken ct)
    {
        HashSet<long>? allowed = null;
        void Intersect(IEnumerable<long> ids)
        {
            if (allowed == null) allowed = new HashSet<long>(ids);
            else allowed.IntersectWith(ids);
        }

        if (filter.Runs.Count > 0) Intersect(filter.Runs);

        if (filter.Batches.Count > 0)
        {
            var batches = filter.Batches;
            var members = await _db.BenchmarkModelBatchMembers.AsNoTracking()
                .Where(b => batches.Contains(b.BenchmarkModelBatchRunId))
                .Select(b => new { b.BenchmarkRunId, b.BenchmarkRunSeriesId, b.BenchmarkBatteryRunId })
                .ToListAsync(ct);
            var runIds = members.Where(m => m.BenchmarkRunId.HasValue).Select(m => m.BenchmarkRunId!.Value).ToHashSet();
            var seriesIds = members.Where(m => m.BenchmarkRunSeriesId.HasValue).Select(m => m.BenchmarkRunSeriesId!.Value).Distinct().ToList();
            var batteryRunIds = members.Where(m => m.BenchmarkBatteryRunId.HasValue).Select(m => m.BenchmarkBatteryRunId!.Value).Distinct().ToList();
            if (seriesIds.Count > 0)
            {
                runIds.UnionWith(await _db.BenchmarkRuns.AsNoTracking()
                    .Where(r => r.RunSeriesId != null && seriesIds.Contains(r.RunSeriesId.Value))
                    .Select(r => r.Id)
                    .ToListAsync(ct));
            }
            if (batteryRunIds.Count > 0)
            {
                runIds.UnionWith(await _db.BenchmarkBatteryRunMembers.AsNoTracking()
                    .Where(m => batteryRunIds.Contains(m.BenchmarkBatteryRunId))
                    .Select(m => m.BenchmarkRunId)
                    .ToListAsync(ct));
            }
            Intersect(runIds);
        }

        if (filter.Conditions.Count > 0 || filter.Variants.Count > 0)
        {
            var runs = await _db.BenchmarkRuns.AsNoTracking()
                .Where(r => r.Answers.Any(a => a.SelfReports.Any(s => s.Fingerprint != null)))
                .Select(r => new { r.Id, r.CandidatePromptOptionsJson, r.InstructionVariant })
                .ToListAsync(ct);
            if (filter.Conditions.Count > 0)
            {
                var conditions = filter.Conditions;
                Intersect(runs.Where(r => conditions.Contains(BenchmarkCandidatePromptOptions.FromJson(r.CandidatePromptOptionsJson).Condition)).Select(r => r.Id));
            }
            if (filter.Variants.Count > 0)
            {
                var variants = filter.Variants;
                Intersect(runs.Where(r => variants.Contains(VariantNameOf(r.InstructionVariant) ?? GnollBenchFeedbackFilter.NoVariant, StringComparer.Ordinal))
                    .Select(r => r.Id));
            }
        }

        return allowed;
    }

    private async Task<Dictionary<long, RunInfo>> LoadRunInfoAsync(IReadOnlyCollection<long> runIds, CancellationToken ct)
    {
        if (runIds.Count == 0) return new Dictionary<long, RunInfo>();
        var ids = runIds.ToList();
        var rows = await _db.BenchmarkRuns.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                r.RunSeriesId,
                SuiteId = r.BenchmarkSuiteIdUsed ?? r.BenchmarkSuiteId,
                r.SuiteName,
                Model = r.TestedModelSnapshot,
                r.CorpusCondition,
                r.InstructionVariant,
                r.HarnessVersion,
                r.CandidatePromptOptionsJson
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new RunInfo
        {
            Id = r.Id,
            SeriesId = r.RunSeriesId,
            SuiteId = r.SuiteId,
            SuiteName = r.SuiteName ?? "",
            ModelId = r.Model?.ModelId ?? "",
            ModelLabel = r.Model.Label() ?? "",
            Provider = r.Model?.Provider ?? "",
            Condition = BenchmarkCandidatePromptOptions.FromJson(r.CandidatePromptOptionsJson).Condition,
            Variant = r.InstructionVariant,
            VariantName = VariantNameOf(r.InstructionVariant),
            Corpus = r.CorpusCondition ?? GnollBenchFeedbackFilter.LiveCorpus,
            Harness = r.HarnessVersion
        });
    }

    private static IQueryable<OccurrenceRow> ProjectOccurrences(IQueryable<BenchmarkRunAnswerSelfReport> query)
        => query.Select(s => new OccurrenceRow
        {
            Id = s.Id,
            Fingerprint = s.Fingerprint!,
            OccurredUtc = s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc,
            RunId = s.BenchmarkRunAnswer.BenchmarkRunId,
            AnswerId = s.BenchmarkRunAnswerId,
            QuestionId = s.BenchmarkRunAnswer.BenchmarkQuestionIdUsed ?? s.BenchmarkRunAnswer.BenchmarkQuestionId,
            QuestionText = s.BenchmarkRunAnswer.QuestionText,
            Accepted = s.Accepted,
            Error = s.Error,
            SortOrder = s.SortOrder,
            IterationIndex = s.IterationIndex,
            Category = s.Category,
            Subject = s.Subject,
            Summary = s.Summary,
            Details = s.Details,
            SearchedFor = s.SearchedFor,
            ExpectedLocation = s.ExpectedLocation,
            RelatedToolsJson = s.RelatedToolsJson,
            NoticedVia = s.NoticedVia,
            SuspectedCause = s.SuspectedCause,
            SuspectedCauseNote = s.SuspectedCauseNote,
            Truncated = s.Truncated
        });

    /// <summary>The rows with their runs' facts, in the rows' order.</summary>
    private async Task<List<GnollBenchFeedbackOccurrence>> CompleteOccurrencesAsync(List<OccurrenceRow> rows, CancellationToken ct)
    {
        var runs = await LoadRunInfoAsync(rows.Select(r => r.RunId).Distinct().ToList(), ct);
        return rows.Select(r =>
        {
            runs.TryGetValue(r.RunId, out var run);
            return new GnollBenchFeedbackOccurrence
            {
                Id = r.Id,
                Fingerprint = r.Fingerprint,
                OccurredUtc = r.OccurredUtc,
                RunId = r.RunId,
                SeriesId = run?.SeriesId,
                AnswerId = r.AnswerId,
                SuiteId = run?.SuiteId,
                SuiteName = run?.SuiteName ?? "",
                QuestionId = r.QuestionId,
                QuestionText = r.QuestionText,
                ModelId = run?.ModelId ?? "",
                ModelLabel = run?.ModelLabel ?? "",
                Provider = run?.Provider ?? "",
                Condition = (run?.Condition ?? BenchmarkInformationCondition.Production).ToWire(),
                Variant = run?.Variant,
                VariantName = run?.VariantName,
                Corpus = run?.Corpus ?? GnollBenchFeedbackFilter.LiveCorpus,
                Harness = run?.Harness,
                Accepted = r.Accepted,
                Error = r.Error,
                SortOrder = r.SortOrder,
                IterationIndex = r.IterationIndex,
                Category = r.Category,
                Subject = r.Subject,
                Summary = r.Summary,
                Details = r.Details,
                SearchedFor = r.SearchedFor,
                ExpectedLocation = r.ExpectedLocation,
                RelatedTools = ParseRelatedTools(r.RelatedToolsJson),
                NoticedVia = r.NoticedVia,
                SuspectedCause = r.SuspectedCause,
                SuspectedCauseNote = r.SuspectedCauseNote,
                Truncated = r.Truncated
            };
        }).ToList();
    }

    private async Task<DateTime?> LastAcceptedAsync(string fingerprint, CancellationToken ct)
    {
        var times = BaseSet(GnollBenchAcceptedScope.Accepted)
            .Where(s => s.Fingerprint == fingerprint)
            .Select(s => s.BenchmarkRunAnswer!.StartedAtUtc ?? s.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc);
        if (!await times.AnyAsync(ct)) return null;
        return await times.MaxAsync(ct);
    }

    // --- Helpers ---

    /// <summary>The variant name of a run stamp, split at the first ':' as <see cref="BenchmarkInstructionVariantCatalog.NameOfStamp"/> does; null for none.</summary>
    public static string? VariantNameOf(string? stamp)
        => string.IsNullOrWhiteSpace(stamp) ? null : BenchmarkInstructionVariantCatalog.NameOfStamp(stamp);

    /// <summary>The lowercase, trimmed fingerprint; empty when it is not 64 hex characters.</summary>
    public static string NormalizeFingerprint(string? fingerprint)
    {
        string fp = fingerprint?.Trim().ToLowerInvariant() ?? "";
        return FingerprintPattern.IsMatch(fp) ? fp : "";
    }

    private static string ConditionLabel(BenchmarkInformationCondition condition) => condition switch
    {
        BenchmarkInformationCondition.SourceOnly => "Source code only",
        BenchmarkInformationCondition.ClosedBook => "Closed book",
        _ => "Production"
    };

    private static string OverseerStatusWire(OverseerFeedbackStatus status) => status switch
    {
        OverseerFeedbackStatus.Acknowledged => GnollBenchFeedbackStatuses.Acknowledged,
        OverseerFeedbackStatus.Resolved => GnollBenchFeedbackStatuses.Resolved,
        OverseerFeedbackStatus.Regressed => GnollBenchFeedbackStatuses.Regressed,
        OverseerFeedbackStatus.Dismissed => GnollBenchFeedbackStatuses.Dismissed,
        _ => GnollBenchFeedbackStatuses.New
    };

    private static IEnumerable<GnollBenchFeedbackFacetValueDto> ValueFacet(IEnumerable<IGrouping<string, RunInfo>> groups,
        Func<IEnumerable<long>, HashSet<string>> fingerprintsOfRuns)
        => groups.Select(g => new GnollBenchFeedbackFacetValueDto
        {
            Value = g.Key,
            Label = g.Key,
            Count = fingerprintsOfRuns(g.Select(r => r.Id)).Count
        });

    private static string Shorten(string text, int max)
    {
        string flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= max ? flat : flat[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// A null value keeps the stored one; an empty or blank one clears it (<paramref name="clear"/>). False when the
    /// trimmed value is longer than <paramref name="max"/>.
    /// </summary>
    private static bool TryNormalizeText(string? value, int max, out string? normalized, out bool clear)
    {
        normalized = null;
        clear = false;
        if (value == null) return true;
        string trimmed = value.Trim();
        if (trimmed.Length == 0) { clear = true; return true; }
        if (trimmed.Length > max) return false;
        normalized = trimmed;
        return true;
    }

    private static GnollBenchFeedbackTriageResult Invalid(string fingerprint, string message)
        => new() { Outcome = GnollBenchFeedbackTriageOutcome.Invalid, Fingerprint = fingerprint, Message = message };

    /// <summary>The stored JSON array of tool names; empty when absent or malformed.</summary>
    private static List<string> ParseRelatedTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            var tools = JsonSerializer.Deserialize<List<string?>>(json);
            return tools?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList() ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
