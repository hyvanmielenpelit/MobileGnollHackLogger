namespace Overseer.Services.Feedback;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>One validated item of a resolution file. A null <see cref="Status"/> keeps the stored status.</summary>
public sealed class FeedbackResolutionItem
{
    public int Index { get; init; }
    public long? Id { get; init; }
    public string Fingerprint { get; init; } = "";

    /// <summary>The lowercase status wire value, or null to keep the stored one.</summary>
    public string? Status { get; init; }
    public bool HasNote { get; init; }

    /// <summary>With <see cref="HasNote"/>: the trimmed note, or null to clear it.</summary>
    public string? Note { get; init; }
    public bool HasResolution { get; init; }
    public string? Resolution { get; init; }
    public bool HasFixedInVersion { get; init; }
    public string? FixedInVersion { get; init; }

    /// <summary>Apply only: the lowercase status the preview showed.</summary>
    public string? ExpectedStatus { get; init; }
    public DateTime? ExpectedTriagedUtc { get; init; }

    /// <summary>The item as messages name it: <c>issues[3] (id 42)</c>.</summary>
    public string Label { get; init; } = "";
}

/// <summary>A validated resolution file.</summary>
public sealed class FeedbackResolutionFile
{
    /// <summary>A <see cref="FeedbackTransferSources"/> value.</summary>
    public string Source { get; init; } = FeedbackTransferSources.Overseer;

    /// <summary>In UTC; null when the file has none.</summary>
    public DateTime? ExportedAtUtc { get; init; }
    public IReadOnlyList<FeedbackResolutionItem> Items { get; init; } = Array.Empty<FeedbackResolutionItem>();
}

public enum FeedbackResolutionOutcome
{
    Ok,
    Invalid,
    Conflict,
}

/// <summary>The result of <see cref="FeedbackResolutionService.PreviewAsync"/>.</summary>
public sealed class FeedbackResolutionPreviewResult
{
    public FeedbackResolutionOutcome Outcome { get; init; }
    public AdminFeedbackResolutionInvalidDto? Invalid { get; init; }
    public AdminFeedbackResolutionPreviewDto? Preview { get; init; }
}

/// <summary>The result of <see cref="FeedbackResolutionService.ApplyAsync"/>.</summary>
public sealed class FeedbackResolutionApplyResult
{
    public FeedbackResolutionOutcome Outcome { get; init; }
    public AdminFeedbackResolutionInvalidDto? Invalid { get; init; }
    public AdminFeedbackResolutionConflictDto? Conflict { get; init; }
    public AdminFeedbackResolutionApplyResultDto? Applied { get; init; }
}

/// <summary>
/// The resolution import of the admin Feedback sub-tabs: validates a resolution file (<c>overseer-feedback-resolutions</c>
/// version 1, rules R1–R9), previews it against the stored issues, and applies it in one transaction.
///
/// <para>For each of status, note, resolution and fixed-in version an absent value keeps the stored one and an empty
/// one clears it. Apply compares every item's expected status and triage time with the stored ones before writing,
/// and writes each issue with a conditional update on the values it read, so an issue triaged, regressed or deleted
/// in between rolls the whole file back and is reported as a conflict. Only items that change something are written;
/// they record the importing administrator and the time. A GnollBench item upserts the fingerprint's triage row:
/// becoming Resolved sets <see cref="BenchmarkFeedbackTriage.ResolvedUtc"/>, staying Resolved keeps it, and any other
/// status clears it. Scoped.</para>
/// </summary>
public sealed class FeedbackResolutionService
{
    public const string FormatName = FeedbackMarkdownExport.ResolutionFormatName;
    public const int FormatVersion = FeedbackMarkdownExport.ResolutionFormatVersion;
    public const int MaxItems = 500;

    private static readonly Regex FingerprintPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly string[] ChoosableStatuses =
    {
        AdminFeedbackStatuses.New, AdminFeedbackStatuses.Acknowledged, AdminFeedbackStatuses.Resolved, AdminFeedbackStatuses.Dismissed
    };

    private readonly ApplicationDbContext _db;
    private readonly GnollBenchFeedbackQuery _gnollBench;
    private readonly TimeProvider _clock;

    public FeedbackResolutionService(ApplicationDbContext db, GnollBenchFeedbackQuery gnollBench)
        : this(db, gnollBench, TimeProvider.System)
    {
    }

    internal FeedbackResolutionService(ApplicationDbContext db, GnollBenchFeedbackQuery gnollBench, TimeProvider clock)
    {
        _db = db;
        _gnollBench = gnollBench;
        _clock = clock;
    }

    // --- Validation ---

    /// <summary>
    /// Validates a file against R1–R9. <paramref name="forApply"/> also requires each item's expected status. False
    /// with every error found, each naming its item.
    /// </summary>
    public static bool TryValidate(AdminFeedbackResolutionRequestDto? request, bool forApply, out FeedbackResolutionFile file,
        out List<AdminFeedbackResolutionErrorDto> errors)
    {
        file = new FeedbackResolutionFile();
        errors = new List<AdminFeedbackResolutionErrorDto>();
        if (request == null)
        {
            errors.Add(FileError("The request body is required."));
            return false;
        }

        foreach (var key in UnknownKeys(request.UnknownKeys))
            errors.Add(FileError($"Unknown key `{key}`."));
        if (request.Format != null && request.Format != FormatName)
            errors.Add(FileError($"format must be `{FormatName}`."));
        if (request.Version != null && request.Version != FormatVersion)
            errors.Add(FileError($"version must be {FormatVersion}."));

        bool hasSource = FeedbackTransferSources.TryParse(request.Source, out var source);
        if (!hasSource)
            errors.Add(FileError($"source must be `{FeedbackTransferSources.Overseer}` or `{FeedbackTransferSources.GnollBench}`."));

        var items = request.Items;
        if (items == null || items.Count == 0)
        {
            errors.Add(FileError("issues must be a non-empty list."));
            return false;
        }
        if (items.Count > MaxItems)
        {
            errors.Add(FileError($"issues can hold at most {MaxItems} items."));
            return false;
        }

        bool overseer = source == FeedbackTransferSources.Overseer;
        var parsed = new List<FeedbackResolutionItem>(items.Count);
        var seenIds = new HashSet<long>();
        var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item == null)
            {
                errors.Add(ItemError(index, $"issues[{index}]: the item is empty."));
                continue;
            }

            string? fingerprint = item.Fingerprint;
            bool fingerprintValid = fingerprint != null && FingerprintPattern.IsMatch(fingerprint);
            string label = item.Id != null
                ? $"issues[{index}] (id {item.Id})"
                : fingerprintValid ? $"issues[{index}] (fingerprint {fingerprint![..12]}…)" : $"issues[{index}]";
            int before = errors.Count;

            foreach (var key in UnknownKeys(item.UnknownKeys))
                errors.Add(ItemError(index, $"{label}: unknown key `{key}`."));

            if (hasSource && overseer)
            {
                if (item.Id == null) errors.Add(ItemError(index, $"{label}: id is required for the Overseer source."));
                else if (item.Id <= 0) errors.Add(ItemError(index, $"{label}: id must be a positive integer."));
                else if (!seenIds.Add(item.Id.Value)) errors.Add(ItemError(index, $"{label}: id {item.Id} appears more than once."));
            }
            else if (hasSource && item.Id != null)
            {
                errors.Add(ItemError(index, $"{label}: id is not allowed for the GnollBench source; GnollBench issues are named by fingerprint."));
            }

            if (string.IsNullOrEmpty(fingerprint)) errors.Add(ItemError(index, $"{label}: fingerprint is required."));
            else if (!fingerprintValid) errors.Add(ItemError(index, $"{label}: fingerprint must be 64 lowercase hexadecimal characters."));
            else if (!seenFingerprints.Add(fingerprint!)) errors.Add(ItemError(index, $"{label}: this fingerprint appears more than once."));

            string? status = null;
            if (item.Status != null)
            {
                string wanted = item.Status.Trim().ToLowerInvariant();
                if (wanted == AdminFeedbackStatuses.Regressed)
                    errors.Add(ItemError(index, $"{label}: status cannot be `regressed`: recurrence is recorded by the system."));
                else if (!ChoosableStatuses.Contains(wanted))
                    errors.Add(ItemError(index, $"{label}: unknown status `{item.Status}`. Use new, acknowledged, resolved or dismissed."));
                else
                    status = wanted;
            }

            bool hasNote = false, hasResolution = false, hasVersion = false;
            string? note = null, resolution = null, fixedInVersion = null;
            if (!FeedbackTriageFields.TryRead(item.Note, FeedbackTriageFields.MaxNoteLength, "note", out hasNote, out note, out var error))
                errors.Add(ItemError(index, $"{label}: {error}"));
            if (!FeedbackTriageFields.TryRead(item.Resolution, FeedbackTriageFields.MaxResolutionLength, "resolution", out hasResolution, out resolution, out error))
                errors.Add(ItemError(index, $"{label}: {error}"));
            if (!FeedbackTriageFields.TryReadFixedInVersion(item.FixedInVersion, out hasVersion, out fixedInVersion, out error))
                errors.Add(ItemError(index, $"{label}: {error}"));

            string? expected = null;
            if (forApply)
            {
                expected = item.ExpectedStatus?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(expected))
                    errors.Add(ItemError(index, $"{label}: expectedStatus is required; preview the file again."));
                else if (!AdminFeedbackStatuses.TryParse(expected, out _))
                    errors.Add(ItemError(index, $"{label}: unknown expectedStatus `{item.ExpectedStatus}`."));
            }

            if (errors.Count > before) continue;
            parsed.Add(new FeedbackResolutionItem
            {
                Index = index,
                Id = item.Id,
                Fingerprint = fingerprint!,
                Status = status,
                HasNote = hasNote,
                Note = note,
                HasResolution = hasResolution,
                Resolution = resolution,
                HasFixedInVersion = hasVersion,
                FixedInVersion = fixedInVersion,
                ExpectedStatus = expected,
                ExpectedTriagedUtc = item.ExpectedTriagedUtc is DateTime t ? AsUtc(t) : null,
                Label = label
            });
        }

        if (errors.Count > 0) return false;
        file = new FeedbackResolutionFile
        {
            Source = source!,
            ExportedAtUtc = request.ExportedAtUtc is DateTime at ? AsUtc(at) : null,
            Items = parsed
        };
        return true;
    }

    // --- Preview ---

    /// <summary>
    /// Validates the file and compares every item with the stored issue: whether it exists and the fingerprint belongs to
    /// the id, its current values, the values after the item, and what changed since <c>exportedAtUtc</c>.
    /// </summary>
    public async Task<FeedbackResolutionPreviewResult> PreviewAsync(AdminFeedbackResolutionRequestDto? request, CancellationToken ct = default)
    {
        if (!TryValidate(request, forApply: false, out var file, out var errors))
            return new FeedbackResolutionPreviewResult { Outcome = FeedbackResolutionOutcome.Invalid, Invalid = InvalidDto(errors) };

        var states = await LoadStatesAsync(file, withSubjects: true, ct);
        bool overseer = file.Source == FeedbackTransferSources.Overseer;
        var since = file.ExportedAtUtc;

        var dto = new AdminFeedbackResolutionPreviewDto
        {
            Source = file.Source,
            ExportedAtUtc = since
        };
        if (since == null) dto.Notices.Add("The file has no exportedAtUtc, so changes since the export cannot be detected.");

        foreach (var item in file.Items)
        {
            var state = states[item.Index];
            var row = new AdminFeedbackResolutionPreviewItemDto
            {
                Index = item.Index,
                Id = item.Id,
                Fingerprint = item.Fingerprint,
                Exists = state.Exists,
                FingerprintMatches = state.Exists && state.FingerprintMatches,
                Category = state.Category,
                Subject = state.Subject
            };

            if (!state.Exists)
            {
                row.Errors.Add(overseer
                    ? $"{item.Label}: no Overseer feedback issue has id {item.Id}."
                    : $"{item.Label}: no GnollBench self-report has this fingerprint.");
            }
            else
            {
                row.Current = new AdminFeedbackResolutionStateDto
                {
                    Status = state.Status,
                    Note = state.Note,
                    Resolution = state.Resolution,
                    FixedInVersion = state.FixedInVersion,
                    TriagedUtc = AsUtc(state.TriagedUtc),
                    LastSeenUtc = AsUtc(state.LastSeenUtc)
                };
                if (since.HasValue)
                {
                    row.OccurrencesSinceExport = state.OccurrencesSince;
                    row.TriagedSinceExport = state.TriagedUtc.HasValue && state.TriagedUtc.Value > since.Value;
                    row.RegressedSinceExport = overseer
                        ? state.LastRegressionUtc.HasValue && state.LastRegressionUtc.Value > since.Value
                        : state.Status == GnollBenchFeedbackStatuses.Regressed && state.LastSeenUtc.HasValue && state.LastSeenUtc.Value > since.Value;
                }

                if (!state.FingerprintMatches)
                {
                    row.Errors.Add($"{item.Label}: the fingerprint does not belong to issue {item.Id}.");
                }
                else
                {
                    var (after, changes) = Plan(item, state);
                    row.After = after;
                    row.Changes = changes;
                    row.HasChanges = changes.Status || changes.Note || changes.Resolution || changes.FixedInVersion;
                    if (!row.HasChanges) row.Notices.Add($"{item.Label}: this item changes nothing.");
                    if (item.Status == AdminFeedbackStatuses.Resolved && after.Resolution == null)
                        row.Notices.Add($"{item.Label}: a resolved issue should say what was done.");
                }
            }

            if (row.Errors.Count > 0) dto.ErrorCount++;
            if (row.HasChanges) dto.ChangeCount++;
            if (row.TriagedSinceExport) dto.TriagedSinceExportCount++;
            dto.Items.Add(row);
        }

        return new FeedbackResolutionPreviewResult { Outcome = FeedbackResolutionOutcome.Ok, Preview = dto };
    }

    // --- Apply ---

    /// <summary>
    /// Validates the file again and applies it in one transaction. Invalid when the file breaks a rule, an item lacks
    /// its expected status, or a fingerprint does not belong to its id; Conflict, with nothing written, when an issue is
    /// gone or its status or triage time differs from the expected one, also when that happens between the check and
    /// the write. <paramref name="userId"/> is recorded as the triager of every changed issue.
    /// </summary>
    public async Task<FeedbackResolutionApplyResult> ApplyAsync(AdminFeedbackResolutionRequestDto? request, string? userId, CancellationToken ct = default)
    {
        if (!TryValidate(request, forApply: true, out var file, out var errors))
            return new FeedbackResolutionApplyResult { Outcome = FeedbackResolutionOutcome.Invalid, Invalid = InvalidDto(errors) };

        bool overseer = file.Source == FeedbackTransferSources.Overseer;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var states = await LoadStatesAsync(file, withSubjects: false, ct);
        var invalid = new List<AdminFeedbackResolutionErrorDto>();
        var conflicts = new List<AdminFeedbackResolutionConflictItemDto>();
        foreach (var item in file.Items)
        {
            var state = states[item.Index];
            if (!state.Exists) conflicts.Add(ConflictOf(item, state, "missing"));
            else if (!state.FingerprintMatches) invalid.Add(ItemError(item.Index, $"{item.Label}: the fingerprint does not belong to issue {item.Id}."));
            else if (item.ExpectedStatus != state.Status || !SameTime(item.ExpectedTriagedUtc, state.TriagedUtc)) conflicts.Add(ConflictOf(item, state, "changed"));
        }
        if (invalid.Count > 0)
        {
            await transaction.RollbackAsync(ct);
            return new FeedbackResolutionApplyResult { Outcome = FeedbackResolutionOutcome.Invalid, Invalid = InvalidDto(invalid) };
        }
        if (conflicts.Count > 0)
        {
            await transaction.RollbackAsync(ct);
            return Conflict(conflicts);
        }

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        int changed = 0, unchanged = 0;
        var created = new List<(FeedbackResolutionItem Item, BenchmarkFeedbackTriage Row)>();
        foreach (var item in file.Items)
        {
            var state = states[item.Index];
            var (after, changes) = Plan(item, state);
            if (!(changes.Status || changes.Note || changes.Resolution || changes.FixedInVersion))
            {
                unchanged++;
                continue;
            }
            changed++;

            if (overseer)
            {
                if (await WriteOverseerAsync(item, state, after, userId, now, ct) == 0)
                    return await ConflictAfterRaceAsync(transaction, file, new[] { item }, ct);
            }
            else if (state.StoredGnollBenchStatus is BenchmarkFeedbackTriageStatus stored)
            {
                if (await WriteGnollBenchAsync(item, state, stored, after, userId, now, ct) == 0)
                    return await ConflictAfterRaceAsync(transaction, file, new[] { item }, ct);
            }
            else
            {
                var row = NewTriageRow(item, state, after, userId, now);
                _db.BenchmarkFeedbackTriages.Add(row);
                created.Add((item, row));
            }
        }

        if (created.Count > 0)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another administrator created the row first; the unique fingerprint index refused this one.
                foreach (var (_, row) in created) _db.Entry(row).State = EntityState.Detached;
                return await ConflictAfterRaceAsync(transaction, file, created.Select(c => c.Item).ToList(), ct);
            }
            foreach (var (_, row) in created) _db.Entry(row).State = EntityState.Detached;
        }

        await transaction.CommitAsync(ct);
        return new FeedbackResolutionApplyResult
        {
            Outcome = FeedbackResolutionOutcome.Ok,
            Applied = new AdminFeedbackResolutionApplyResultDto { Changed = changed, Unchanged = unchanged }
        };
    }

    // --- State ---

    private sealed class CurrentState
    {
        public bool Exists { get; init; }
        public bool FingerprintMatches { get; init; } = true;

        /// <summary>The shown status wire value.</summary>
        public string Status { get; init; } = AdminFeedbackStatuses.New;
        public string? Note { get; init; }
        public string? Resolution { get; init; }
        public string? FixedInVersion { get; init; }
        public DateTime? TriagedUtc { get; init; }
        public DateTime? LastSeenUtc { get; init; }
        public DateTime? LastRegressionUtc { get; init; }
        public DateTime? ResolvedUtc { get; init; }
        public string? Category { get; init; }
        public string? Subject { get; init; }
        public OverseerFeedbackStatus OverseerStatus { get; init; }

        /// <summary>GnollBench: the stored triage status; null without a triage row.</summary>
        public BenchmarkFeedbackTriageStatus? StoredGnollBenchStatus { get; init; }

        /// <summary>Occurrences newer than the export; 0 when the file has no export time.</summary>
        public int OccurrencesSince { get; init; }
    }

    /// <summary>The stored state of every item, keyed by the item's index. Untracked.</summary>
    private async Task<Dictionary<int, CurrentState>> LoadStatesAsync(FeedbackResolutionFile file, bool withSubjects, CancellationToken ct)
    {
        var result = new Dictionary<int, CurrentState>();
        var since = file.ExportedAtUtc;

        if (file.Source == FeedbackTransferSources.Overseer)
        {
            var ids = file.Items.Select(i => i.Id!.Value).Distinct().ToList();
            var issues = await _db.OverseerFeedbackIssues.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    i.Fingerprint,
                    i.Status,
                    i.TriageNote,
                    i.Resolution,
                    i.FixedInVersion,
                    i.TriagedUtc,
                    i.LastSeenUtc,
                    i.LastRegressionUtc,
                    i.Category,
                    i.Subject
                })
                .ToDictionaryAsync(i => i.Id, ct);

            var counts = new Dictionary<long, int>();
            if (since.HasValue && issues.Count > 0)
            {
                var at = since.Value;
                var found = issues.Keys.ToList();
                counts = await _db.OverseerFeedbackReports.AsNoTracking()
                    .Where(r => found.Contains(r.IssueId) && r.CreatedUtc > at)
                    .GroupBy(r => r.IssueId)
                    .Select(g => new { IssueId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.IssueId, x => x.Count, ct);
            }

            foreach (var item in file.Items)
            {
                if (!issues.TryGetValue(item.Id!.Value, out var issue))
                {
                    result[item.Index] = new CurrentState { Exists = false };
                    continue;
                }
                result[item.Index] = new CurrentState
                {
                    Exists = true,
                    FingerprintMatches = string.Equals(issue.Fingerprint, item.Fingerprint, StringComparison.Ordinal),
                    Status = AdminFeedbackStatuses.ToWire(issue.Status),
                    OverseerStatus = issue.Status,
                    Note = issue.TriageNote,
                    Resolution = issue.Resolution,
                    FixedInVersion = issue.FixedInVersion,
                    TriagedUtc = issue.TriagedUtc,
                    LastSeenUtc = issue.LastSeenUtc,
                    LastRegressionUtc = issue.LastRegressionUtc,
                    Category = issue.Category,
                    Subject = issue.Subject,
                    OccurrencesSince = counts.GetValueOrDefault(issue.Id)
                };
            }
            return result;
        }

        var fingerprints = file.Items.Select(i => i.Fingerprint).ToList();
        var states = await _gnollBench.GetTriageStatesAsync(fingerprints, ct);
        var sinceCounts = since.HasValue
            ? await _gnollBench.CountAcceptedSinceAsync(fingerprints, since.Value, ct)
            : new Dictionary<string, int>();
        var subjects = new Dictionary<string, GnollBenchFeedbackIssue>(StringComparer.Ordinal);
        if (withSubjects)
        {
            var existing = states.Values.Where(s => s.Exists).Select(s => s.Fingerprint).ToList();
            if (existing.Count > 0)
                foreach (var issue in await _gnollBench.LoadIssuesAsync(existing, 0, GnollBenchAcceptedScope.All, ct))
                    subjects[issue.Fingerprint] = issue;
        }

        foreach (var item in file.Items)
        {
            if (!states.TryGetValue(item.Fingerprint, out var state) || !state.Exists)
            {
                result[item.Index] = new CurrentState { Exists = false };
                continue;
            }
            subjects.TryGetValue(item.Fingerprint, out var issue);
            result[item.Index] = new CurrentState
            {
                Exists = true,
                Status = GnollBenchFeedbackStatuses.ToWire(state.Status),
                StoredGnollBenchStatus = state.StoredStatus,
                Note = state.TriageNote,
                Resolution = state.Resolution,
                FixedInVersion = state.FixedInVersion,
                TriagedUtc = state.TriagedUtc,
                ResolvedUtc = state.ResolvedUtc,
                LastSeenUtc = state.LastSeenUtc,
                Category = issue?.Category,
                Subject = issue?.Subject,
                OccurrencesSince = sinceCounts.GetValueOrDefault(item.Fingerprint)
            };
        }
        return result;
    }

    /// <summary>The values after <paramref name="item"/> and which of them it changes, against the shown status.</summary>
    private static (AdminFeedbackResolutionValuesDto After, AdminFeedbackResolutionChangesDto Changes) Plan(FeedbackResolutionItem item, CurrentState state)
    {
        var after = new AdminFeedbackResolutionValuesDto
        {
            Status = item.Status ?? state.Status,
            Note = item.HasNote ? item.Note : state.Note,
            Resolution = item.HasResolution ? item.Resolution : state.Resolution,
            FixedInVersion = item.HasFixedInVersion ? item.FixedInVersion : state.FixedInVersion
        };
        var changes = new AdminFeedbackResolutionChangesDto
        {
            Status = after.Status != state.Status,
            Note = !string.Equals(after.Note, state.Note, StringComparison.Ordinal),
            Resolution = !string.Equals(after.Resolution, state.Resolution, StringComparison.Ordinal),
            FixedInVersion = !string.Equals(after.FixedInVersion, state.FixedInVersion, StringComparison.Ordinal)
        };
        return (after, changes);
    }

    // --- Writes ---

    /// <summary>Writes one Overseer issue if its status and triage time are still the ones read; the number of rows written.</summary>
    private Task<int> WriteOverseerAsync(FeedbackResolutionItem item, CurrentState state, AdminFeedbackResolutionValuesDto after, string? userId,
        DateTime now, CancellationToken ct)
    {
        long id = item.Id!.Value;
        var storedStatus = state.OverseerStatus;
        DateTime? storedTriaged = state.TriagedUtc;
        var status = item.Status != null && AdminFeedbackStatuses.TryParse(item.Status, out var parsed) ? parsed : storedStatus;
        string? note = after.Note, resolution = after.Resolution, fixedInVersion = after.FixedInVersion;
        DateTime? triagedUtc = now;

        return _db.OverseerFeedbackIssues
            .Where(i => i.Id == id && i.Status == storedStatus && i.TriagedUtc == storedTriaged)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, status)
                .SetProperty(i => i.TriageNote, note)
                .SetProperty(i => i.Resolution, resolution)
                .SetProperty(i => i.FixedInVersion, fixedInVersion)
                .SetProperty(i => i.TriagedByUserId, userId)
                .SetProperty(i => i.TriagedUtc, triagedUtc), ct);
    }

    /// <summary>Writes one existing triage row if its status and triage time are still the ones read; the number of rows written.</summary>
    private Task<int> WriteGnollBenchAsync(FeedbackResolutionItem item, CurrentState state, BenchmarkFeedbackTriageStatus stored,
        AdminFeedbackResolutionValuesDto after, string? userId, DateTime now, CancellationToken ct)
    {
        string fingerprint = item.Fingerprint;
        DateTime? storedTriaged = state.TriagedUtc;
        var status = StoredStatusAfter(item, stored);
        DateTime? resolvedUtc = ResolvedUtcAfter(item, state, status, now);
        string? note = after.Note, resolution = after.Resolution, fixedInVersion = after.FixedInVersion;
        DateTime? triagedUtc = now;

        return _db.BenchmarkFeedbackTriages
            .Where(t => t.Fingerprint == fingerprint && t.Status == stored && t.TriagedUtc == storedTriaged)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, status)
                .SetProperty(t => t.TriageNote, note)
                .SetProperty(t => t.Resolution, resolution)
                .SetProperty(t => t.FixedInVersion, fixedInVersion)
                .SetProperty(t => t.ResolvedUtc, resolvedUtc)
                .SetProperty(t => t.TriagedByUserId, userId)
                .SetProperty(t => t.TriagedUtc, triagedUtc), ct);
    }

    private static BenchmarkFeedbackTriage NewTriageRow(FeedbackResolutionItem item, CurrentState state, AdminFeedbackResolutionValuesDto after,
        string? userId, DateTime now)
    {
        var status = StoredStatusAfter(item, BenchmarkFeedbackTriageStatus.New);
        return new BenchmarkFeedbackTriage
        {
            Fingerprint = item.Fingerprint,
            Status = status,
            TriageNote = after.Note,
            Resolution = after.Resolution,
            FixedInVersion = after.FixedInVersion,
            ResolvedUtc = ResolvedUtcAfter(item, state, status, now),
            TriagedByUserId = userId,
            TriagedUtc = now
        };
    }

    private static BenchmarkFeedbackTriageStatus StoredStatusAfter(FeedbackResolutionItem item, BenchmarkFeedbackTriageStatus stored) => item.Status switch
    {
        null => stored,
        AdminFeedbackStatuses.Acknowledged => BenchmarkFeedbackTriageStatus.Acknowledged,
        AdminFeedbackStatuses.Resolved => BenchmarkFeedbackTriageStatus.Resolved,
        AdminFeedbackStatuses.Dismissed => BenchmarkFeedbackTriageStatus.Dismissed,
        _ => BenchmarkFeedbackTriageStatus.New
    };

    /// <summary>
    /// Becoming Resolved (from any other shown status, Regressed included) is now; staying Resolved keeps the stored
    /// time; any other status clears it.
    /// </summary>
    private static DateTime? ResolvedUtcAfter(FeedbackResolutionItem item, CurrentState state, BenchmarkFeedbackTriageStatus status, DateTime now)
    {
        if (status != BenchmarkFeedbackTriageStatus.Resolved) return null;
        bool resolving = item.Status == AdminFeedbackStatuses.Resolved
            && (state.Status != GnollBenchFeedbackStatuses.Resolved || state.ResolvedUtc == null);
        return resolving ? now : state.ResolvedUtc;
    }

    // --- Results ---

    private async Task<FeedbackResolutionApplyResult> ConflictAfterRaceAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        FeedbackResolutionFile file, IReadOnlyCollection<FeedbackResolutionItem> raced, CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);
        var states = await LoadStatesAsync(file, withSubjects: false, ct);
        return Conflict(raced.Select(item => ConflictOf(item, states[item.Index], states[item.Index].Exists ? "changed" : "missing")).ToList());
    }

    private static FeedbackResolutionApplyResult Conflict(List<AdminFeedbackResolutionConflictItemDto> conflicts)
        => new()
        {
            Outcome = FeedbackResolutionOutcome.Conflict,
            Conflict = new AdminFeedbackResolutionConflictDto
            {
                Message = conflicts.Count == 1
                    ? "One issue changed after the preview, so nothing was applied. Review the file again."
                    : $"{conflicts.Count} issues changed after the preview, so nothing was applied. Review the file again.",
                Conflicts = conflicts
            }
        };

    private static AdminFeedbackResolutionConflictItemDto ConflictOf(FeedbackResolutionItem item, CurrentState state, string reason)
        => new()
        {
            Index = item.Index,
            Id = item.Id,
            Fingerprint = item.Fingerprint,
            Reason = reason,
            ExpectedStatus = item.ExpectedStatus,
            CurrentStatus = state.Exists ? state.Status : null,
            ExpectedTriagedUtc = AsUtc(item.ExpectedTriagedUtc),
            CurrentTriagedUtc = state.Exists ? AsUtc(state.TriagedUtc) : null
        };

    private static AdminFeedbackResolutionInvalidDto InvalidDto(List<AdminFeedbackResolutionErrorDto> errors)
        => new()
        {
            Message = errors.Count == 1 ? errors[0].Message : $"The file has {errors.Count} errors.",
            Errors = errors
        };

    private static AdminFeedbackResolutionErrorDto FileError(string message) => new() { Message = message };

    private static AdminFeedbackResolutionErrorDto ItemError(int index, string message) => new() { Index = index, Message = message };

    // --- Helpers ---

    private static IEnumerable<string> UnknownKeys(Dictionary<string, System.Text.Json.JsonElement>? extra)
        => extra == null ? Enumerable.Empty<string>() : extra.Keys.OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>Equal to the millisecond, so a time that went through a client's date type still matches.</summary>
    private static bool SameTime(DateTime? expected, DateTime? stored)
    {
        if (expected.HasValue != stored.HasValue) return false;
        if (!expected.HasValue) return true;
        return Math.Abs((AsUtc(expected.Value) - AsUtc(stored!.Value)).Ticks) < TimeSpan.TicksPerMillisecond;
    }

    /// <summary>The value as UTC; a stored time without a kind is UTC.</summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    private static DateTime? AsUtc(DateTime? value) => value is DateTime v ? AsUtc(v) : null;
}
