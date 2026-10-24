namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using StoredChange = MobileGnollHackLogger.Data.BenchmarkExperimentalChange;

/// <summary>What a store operation did; the controller maps it onto a status code.</summary>
public enum BenchmarkChangeStoreOutcome
{
    Saved,
    Created,
    NotFound,
    Invalid,
    Conflict
}

/// <summary>The <c>reason</c> of a 409 from the experimental-change editor.</summary>
public static class BenchmarkChangeConflictReasons
{
    public const string Locked = "locked";
    public const string EditedElsewhere = "editedElsewhere";
    public const string BuiltIn = "builtIn";
}

/// <summary>The result of a store operation: the saved change and its overlaps, or why it was refused.</summary>
public sealed class BenchmarkChangeStoreResult
{
    public BenchmarkChangeStoreOutcome Outcome { get; init; }
    public BenchmarkExperimentalChange? Change { get; init; }
    public IReadOnlyList<BenchmarkChangeOverlap> Overlaps { get; init; } = Array.Empty<BenchmarkChangeOverlap>();
    public string? Error { get; init; }

    /// <summary>Field name → message, for an <see cref="BenchmarkChangeStoreOutcome.Invalid"/> save.</summary>
    public IReadOnlyDictionary<string, string>? FieldErrors { get; init; }

    /// <summary>One of <see cref="BenchmarkChangeConflictReasons"/> for a <see cref="BenchmarkChangeStoreOutcome.Conflict"/>.</summary>
    public string? ConflictReason { get; init; }

    public bool Succeeded => Outcome is BenchmarkChangeStoreOutcome.Saved or BenchmarkChangeStoreOutcome.Created;
}

/// <summary>
/// Creates, edits, retires, restores and deletes the experimental changes written in Overseer, locks
/// them when a run is stamped with them, and packs any change as a repository folder. Every write
/// refreshes the catalog. A save that creates or changes text needs the generic-text confirmation;
/// an edit is a conditional update on the expected <c>UpdatedAtUtc</c>, and a text edit also on the
/// change being unlocked.
/// </summary>
public class BenchmarkExperimentalChangeStore
{
    /// <summary>The longest stem an id generated from a title keeps before its suffix.</summary>
    public const int IdStemMaxLength = 26;

    public const string ConfirmationRequired =
        "Confirm that the text is a general procedure or wording that states no game fact, answer or hint about a particular question.";

    public const string LockedMessage =
        "Its text is locked: a run has used it, and that run stays re-runnable only while the text is unchanged. Copy it as a new version to change the text.";

    public const string EditedElsewhereMessage =
        "It was changed elsewhere since you opened it. Reload it and make your edit again.";

    public const string BuiltInMessage = "This change is now built in; change it in the repository.";

    public const string FieldErrorsMessage = "Some fields need attention.";

    private static readonly Regex VersionedIdPattern = new("^(.+)-v([1-9][0-9]*)$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions FolderJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkExperimentalChangeCatalog _catalog;
    private readonly BenchmarkChangeTextScreen _screen;
    private readonly TimeProvider _time;

    public BenchmarkExperimentalChangeStore(
        ApplicationDbContext db,
        BenchmarkExperimentalChangeCatalog catalog,
        BenchmarkChangeTextScreen screen,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _catalog = catalog;
        _screen = screen;
        _time = timeProvider ?? TimeProvider.System;
    }

    public BenchmarkExperimentalChangeCatalog Catalog => _catalog;

    // --- Create and edit ----------------------------------------------------------------------

    /// <summary>
    /// Creates a change from <paramref name="request"/>. Its id is generated from the title, or, for
    /// <c>copiedFrom</c>, is the next free version of the copied change's id.
    /// </summary>
    public async Task<BenchmarkChangeStoreResult> CreateAsync(BenchmarkExperimentalChangeEditRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _catalog.RefreshAsync(ct);

        string? copiedFrom = string.IsNullOrWhiteSpace(request.CopiedFrom) ? null : request.CopiedFrom.Trim();
        BenchmarkExperimentalChange? source = null;
        if (copiedFrom != null)
        {
            source = _catalog.Snapshot.Find(copiedFrom) ?? _catalog.BuiltIns.FirstOrDefault(b => b.Id == copiedFrom);
            if (source == null)
                return Invalid($"Unknown experimental change '{copiedFrom}' to copy.");
            if (source.HasCodeEffect)
                return Invalid("Only a change without a new tool or tool-output switch can be copied as a new version.");
            if (!source.IsValid)
                return Invalid($"'{copiedFrom}' is invalid, so it cannot be copied: {source.InvalidReason}");
        }

        var (draft, fieldErrors) = Draft(request, "draft-v1");
        if (!request.GenericConfirmed) fieldErrors.TryAdd("genericConfirmed", ConfirmationRequired);
        if (fieldErrors.Count > 0) return Invalid(FieldErrorsMessage, fieldErrors);

        StoredChange? row = null;
        for (int attempt = 0; ; attempt++)
        {
            var taken = await TakenIdsAsync(ct);
            string id = (source != null ? NextVersionId(source.Id, taken) : null) ?? GenerateId(draft.Title, taken);
            var change = draft with { Id = id };
            var now = Now();
            row = new StoredChange
            {
                ChangeId = id,
                Title = change.Title,
                Summary = change.Summary,
                ExpectedEffect = change.ExpectedEffect,
                DescriptionEditsJson = SerializeEdits(change.DescriptionEdits),
                Instructions = change.Instructions,
                ContentHash = BenchmarkExperimentalChangeHash.Of(change),
                CopiedFromChangeId = source?.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.BenchmarkExperimentalChanges.Add(row);
            try
            {
                await _db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Another request took the id between the check and the insert.
                _db.Entry(row).State = EntityState.Detached;
            }
        }

        return await SavedAsync(BenchmarkChangeStoreOutcome.Created, row!, ct);
    }

    /// <summary>
    /// Edits change <paramref name="id"/>. A locked change accepts only title, summary and Should
    /// improve; a built-in change, or a stored one a built-in folder of equal text displaces, is refused.
    /// </summary>
    public async Task<BenchmarkChangeStoreResult> UpdateAsync(string id, BenchmarkExperimentalChangeEditRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _catalog.RefreshAsync(ct);

        var (row, refusal) = await FindEditableAsync(id, ct);
        if (refusal != null) return refusal;

        var current = _catalog.FromStored(row!);
        var (draft, fieldErrors) = Draft(request, row!.ChangeId);
        string draftHash = BenchmarkExperimentalChangeHash.Of(draft);
        bool textChanged = !string.Equals(draftHash, current.Hash, StringComparison.Ordinal);

        if (textChanged && row.LockedAtUtc != null) return Conflict(BenchmarkChangeConflictReasons.Locked, LockedMessage);
        if (request.ExpectedUpdatedAtUtc is not DateTime expected || expected != row.UpdatedAtUtc)
            return Conflict(BenchmarkChangeConflictReasons.EditedElsewhere, EditedElsewhereMessage);

        if (textChanged && !request.GenericConfirmed) fieldErrors.TryAdd("genericConfirmed", ConfirmationRequired);
        if (fieldErrors.Count > 0) return Invalid(FieldErrorsMessage, fieldErrors);

        DateTime stored = row.UpdatedAtUtc;
        var now = Now();
        if (now <= stored) now = stored.AddMilliseconds(1);
        string changeId = row.ChangeId;
        string title = draft.Title;
        string summary = draft.Summary;
        string? expectedEffect = draft.ExpectedEffect;

        int affected;
        if (textChanged)
        {
            string? editsJson = SerializeEdits(draft.DescriptionEdits);
            string? instructions = draft.Instructions;
            affected = await _db.BenchmarkExperimentalChanges
                .Where(c => c.ChangeId == changeId && c.LockedAtUtc == null && c.UpdatedAtUtc == stored)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Title, title)
                    .SetProperty(c => c.Summary, summary)
                    .SetProperty(c => c.ExpectedEffect, expectedEffect)
                    .SetProperty(c => c.DescriptionEditsJson, editsJson)
                    .SetProperty(c => c.Instructions, instructions)
                    .SetProperty(c => c.ContentHash, draftHash)
                    .SetProperty(c => c.UpdatedAtUtc, now), ct);
        }
        else
        {
            affected = await _db.BenchmarkExperimentalChanges
                .Where(c => c.ChangeId == changeId && c.UpdatedAtUtc == stored)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Title, title)
                    .SetProperty(c => c.Summary, summary)
                    .SetProperty(c => c.ExpectedEffect, expectedEffect)
                    .SetProperty(c => c.UpdatedAtUtc, now), ct);
        }

        if (affected == 0)
        {
            var again = await FindRowAsync(changeId, ct);
            if (again == null) return NotFound(changeId);
            return textChanged && again.LockedAtUtc != null
                ? Conflict(BenchmarkChangeConflictReasons.Locked, LockedMessage)
                : Conflict(BenchmarkChangeConflictReasons.EditedElsewhere, EditedElsewhereMessage);
        }

        var saved = await FindRowAsync(changeId, ct);
        return saved == null ? NotFound(changeId) : await SavedAsync(BenchmarkChangeStoreOutcome.Saved, saved, ct);
    }

    // --- Retire, restore, delete --------------------------------------------------------------

    /// <summary>Hides a change from the picker and refuses it in a new launch; re-runs still resolve it.</summary>
    public Task<BenchmarkChangeStoreResult> RetireAsync(string id, CancellationToken ct = default)
        => SetRetiredAsync(id, retired: true, ct);

    public Task<BenchmarkChangeStoreResult> RestoreAsync(string id, CancellationToken ct = default)
        => SetRetiredAsync(id, retired: false, ct);

    private async Task<BenchmarkChangeStoreResult> SetRetiredAsync(string id, bool retired, CancellationToken ct)
    {
        await _catalog.RefreshAsync(ct);
        var (row, refusal) = await FindEditableAsync(id, ct, builtInMessage: "A built-in change is retired and restored in the repository's retired.json.");
        if (refusal != null) return refusal;

        string changeId = row!.ChangeId;
        DateTime? retiredAt = retired ? Now() : null;
        await _db.BenchmarkExperimentalChanges
            .Where(c => c.ChangeId == changeId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RetiredAtUtc, retiredAt), ct);
        await _catalog.RefreshAsync(ct);
        return new BenchmarkChangeStoreResult { Outcome = BenchmarkChangeStoreOutcome.Saved, Change = _catalog.Snapshot.Find(changeId) };
    }

    /// <summary>Deletes a change no run has used; a locked one is refused.</summary>
    public async Task<BenchmarkChangeStoreResult> DeleteAsync(string id, CancellationToken ct = default)
    {
        await _catalog.RefreshAsync(ct);
        var (row, refusal) = await FindEditableAsync(id, ct);
        if (refusal != null) return refusal;

        string changeId = row!.ChangeId;
        int deleted = await _db.BenchmarkExperimentalChanges
            .Where(c => c.ChangeId == changeId && c.LockedAtUtc == null)
            .ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            return await FindRowAsync(changeId, ct) == null
                ? NotFound(changeId)
                : Conflict(BenchmarkChangeConflictReasons.Locked, "A run has used it, so it cannot be deleted. Retire it instead.");
        }

        await _catalog.RefreshAsync(ct);
        return new BenchmarkChangeStoreResult { Outcome = BenchmarkChangeStoreOutcome.Saved };
    }

    // --- Locking ------------------------------------------------------------------------------

    /// <summary>
    /// Sets <c>LockedAtUtc</c> on each of <paramref name="changeIds"/> that is not locked yet, in one
    /// conditional update. A lock is never removed.
    /// </summary>
    public static async Task<int> LockAsync(
        ApplicationDbContext db, IReadOnlyCollection<string> changeIds, DateTime nowUtc, CancellationToken ct = default)
    {
        if (changeIds.Count == 0) return 0;
        var ids = changeIds.Distinct(StringComparer.Ordinal).ToList();
        DateTime? lockedAt = nowUtc;
        return await db.BenchmarkExperimentalChanges
            .Where(c => ids.Contains(c.ChangeId) && c.LockedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LockedAtUtc, lockedAt), ct);
    }

    // --- Ids ----------------------------------------------------------------------------------

    /// <summary>
    /// The id generated from <paramref name="title"/>: lower-case ASCII, every run of other
    /// characters a <c>-</c>, trimmed to <see cref="IdStemMaxLength"/> characters at a word boundary,
    /// then <c>-v1</c>; when taken, <c>-2</c>, <c>-3</c>… before <c>-v1</c>.
    /// </summary>
    public static string GenerateId(string title, IReadOnlySet<string> taken)
    {
        string stem = Stem(title);
        for (int n = 1; ; n++)
        {
            string id = n == 1 ? $"{stem}-v1" : $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}-v1";
            if (!taken.Contains(id)) return id;
        }
    }

    /// <summary>The next free <c>x-v&lt;M&gt;</c> after <c>x-v&lt;N&gt;</c>; null when none fits the id length.</summary>
    public static string? NextVersionId(string id, IReadOnlySet<string> taken)
    {
        var match = VersionedIdPattern.Match(id);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            return null;

        string stem = match.Groups[1].Value;
        for (int next = version + 1; next < version + 1000; next++)
        {
            string candidate = $"{stem}-v{next.ToString(CultureInfo.InvariantCulture)}";
            if (candidate.Length > BenchmarkExperimentalChangeValidator.IdMaxLength) return null;
            if (!taken.Contains(candidate)) return candidate;
        }
        return null;
    }

    internal static string Stem(string title)
    {
        var sb = new StringBuilder();
        bool pendingDash = false;
        foreach (char ch in (title ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            char c = char.ToLowerInvariant(ch);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                if (pendingDash && sb.Length > 0) sb.Append('-');
                pendingDash = false;
                sb.Append(c);
            }
            else
            {
                pendingDash = true;
            }
        }

        string stem = sb.ToString();
        if (stem.Length > IdStemMaxLength)
        {
            if (stem[IdStemMaxLength] == '-')
            {
                stem = stem[..IdStemMaxLength];
            }
            else
            {
                int cut = stem.LastIndexOf('-', IdStemMaxLength - 1);
                stem = cut > 0 ? stem[..cut] : stem[..IdStemMaxLength];
            }
        }
        stem = stem.Trim('-');
        return stem.Length == 0 ? "change" : stem;
    }

    private async Task<IReadOnlySet<string>> TakenIdsAsync(CancellationToken ct)
    {
        var taken = new HashSet<string>(_catalog.BuiltIns.Select(b => b.Id), StringComparer.Ordinal);
        foreach (string id in await _db.BenchmarkExperimentalChanges.AsNoTracking().Select(c => c.ChangeId).ToListAsync(ct))
        {
            taken.Add(id);
        }
        return taken;
    }

    // --- Download as folder -------------------------------------------------------------------

    /// <summary>
    /// <c>&lt;id&gt;/change.json</c> and its <c>.md</c> files (<c>&lt;tool&gt;.md</c> per description
    /// edit and for an experimental tool's description, <c>instructions.md</c>) as a zip, UTF-8 without
    /// a BOM, CRLF: a folder to put in <c>Overseer/BenchmarkChanges/</c>.
    /// </summary>
    public static byte[] BuildFolderZip(BenchmarkExperimentalChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var additions = change.DescriptionEdits.Where(e => !e.IsReplacement).OrderBy(e => e.Tool, StringComparer.Ordinal).ToList();
        var replacements = change.DescriptionEdits.Where(e => e.IsReplacement).OrderBy(e => e.Tool, StringComparer.Ordinal).ToList();
        var file = new BenchmarkExperimentalChangeFile
        {
            Id = change.Id,
            Title = change.Title,
            Summary = change.Summary,
            ExpectedEffect = change.ExpectedEffect,
            ExperimentalTool = change.ExperimentalTool,
            ExperimentalToolDescription = change.ExperimentalTool != null && change.ExperimentalToolDescription != null
                ? MarkdownFileOf(change.ExperimentalTool)
                : null,
            Experiment = change.Experiment,
            DescriptionAdditions = additions.ToDictionary(e => e.Tool, e => MarkdownFileOf(e.Tool)),
            DescriptionReplacements = replacements.ToDictionary(e => e.Tool, e => MarkdownFileOf(e.Tool)),
            Instructions = change.Instructions != null ? "instructions.md" : null
        };

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, $"{change.Id}/{BenchmarkExperimentalChangeCatalog.ChangeFileName}", JsonSerializer.Serialize(file, FolderJsonOptions));
            if (file.ExperimentalToolDescription != null)
                Add(zip, $"{change.Id}/{file.ExperimentalToolDescription}", change.ExperimentalToolDescription!);
            foreach (var edit in additions.Concat(replacements))
                Add(zip, $"{change.Id}/{MarkdownFileOf(edit.Tool)}", edit.Text);
            if (change.Instructions != null)
                Add(zip, $"{change.Id}/instructions.md", change.Instructions);
        }
        return buffer.ToArray();
    }

    private static string MarkdownFileOf(string tool) => tool + ".md";

    private static void Add(ZipArchive zip, string name, string text)
    {
        string crlf = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Replace("\n", "\r\n") + "\r\n";
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        byte[] bytes = Utf8NoBom.GetBytes(crlf);
        stream.Write(bytes, 0, bytes.Length);
    }

    // --- Helpers ------------------------------------------------------------------------------

    /// <summary>
    /// The request as a change with id <paramref name="id"/>, its texts normalized, and its field
    /// errors (the id rules are not checked).
    /// </summary>
    private (BenchmarkExperimentalChange Draft, Dictionary<string, string> FieldErrors) Draft(
        BenchmarkExperimentalChangeEditRequest request, string id)
    {
        var fieldErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var edits = new List<BenchmarkChangeDescriptionEdit>();
        var requested = request.DescriptionEdits ?? new List<BenchmarkChangeDescriptionEditRequest>();
        for (int i = 0; i < requested.Count; i++)
        {
            var item = requested[i] ?? new BenchmarkChangeDescriptionEditRequest();
            if (!BenchmarkExperimentalChangeWire.TryParseMode(item.Mode, out var mode))
            {
                fieldErrors.TryAdd("effects", $"A description edit is '{BenchmarkExperimentalChangeWire.Add}' or '{BenchmarkExperimentalChangeWire.Replace}'.");
            }
            edits.Add(new BenchmarkChangeDescriptionEdit(
                _catalog.CanonicalToolName(item.Tool ?? string.Empty),
                mode,
                BenchmarkExperimentalChangeHash.Normalize(item.Text) ?? string.Empty));
        }

        var draft = new BenchmarkExperimentalChange
        {
            Id = id,
            Title = request.Title?.Trim() ?? string.Empty,
            Summary = BenchmarkExperimentalChangeHash.Normalize(request.Summary) ?? string.Empty,
            ExpectedEffect = BenchmarkExperimentalChangeHash.NormalizeOrNull(request.ExpectedEffect),
            Source = BenchmarkExperimentalChangeSource.Database,
            DescriptionEdits = edits,
            Instructions = BenchmarkExperimentalChangeHash.NormalizeOrNull(request.Instructions)
        };
        draft = draft with { Hash = BenchmarkExperimentalChangeHash.Of(draft) };

        foreach (var problem in BenchmarkExperimentalChangeValidator.Check(draft, _catalog.Handlers, _catalog.AllowedTools, checkId: false))
        {
            fieldErrors.TryAdd(problem.Field, problem.Message);
        }
        return (draft, fieldErrors);
    }

    private static string? SerializeEdits(IReadOnlyList<BenchmarkChangeDescriptionEdit> edits)
        => edits.Count == 0
            ? null
            : JsonSerializer.Serialize(edits.Select(e => new BenchmarkStoredDescriptionEdit
            {
                Tool = e.Tool,
                Mode = BenchmarkExperimentalChangeWire.ModeName(e.Mode),
                Text = e.Text
            }).ToList());

    /// <summary>
    /// The stored row of <paramref name="id"/>, or the refusal: 404 for an unknown id, 409 for a
    /// built-in change or a row a built-in folder of equal text displaces.
    /// </summary>
    private async Task<(StoredChange? Row, BenchmarkChangeStoreResult? Refusal)> FindEditableAsync(
        string id, CancellationToken ct, string builtInMessage = BuiltInMessage)
    {
        var row = await FindRowAsync(id, ct);
        var builtIn = _catalog.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));
        if (row == null)
        {
            return (null, builtIn != null ? Conflict(BenchmarkChangeConflictReasons.BuiltIn, builtInMessage) : NotFound(id));
        }
        if (builtIn != null && builtIn.IsValid
            && string.Equals(builtIn.Hash, _catalog.FromStored(row).Hash, StringComparison.Ordinal))
        {
            return (null, Conflict(BenchmarkChangeConflictReasons.BuiltIn, BuiltInMessage));
        }
        return (row, null);
    }

    private Task<StoredChange?> FindRowAsync(string id, CancellationToken ct)
        => _db.BenchmarkExperimentalChanges.AsNoTracking().FirstOrDefaultAsync(c => c.ChangeId == id, ct);

    private async Task<BenchmarkChangeStoreResult> SavedAsync(BenchmarkChangeStoreOutcome outcome, StoredChange row, CancellationToken ct)
    {
        await _catalog.RefreshAsync(ct);
        var change = _catalog.Snapshot.Find(row.ChangeId) ?? _catalog.FromStored(row);
        var overlaps = await _screen.ScreenAsync(change.Texts, ct);
        return new BenchmarkChangeStoreResult { Outcome = outcome, Change = change, Overlaps = overlaps };
    }

    /// <summary>Now, truncated to whole milliseconds, so the value survives a round trip through a browser.</summary>
    private DateTime Now()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private static BenchmarkChangeStoreResult Invalid(string error, IReadOnlyDictionary<string, string>? fieldErrors = null)
        => new() { Outcome = BenchmarkChangeStoreOutcome.Invalid, Error = error, FieldErrors = fieldErrors };

    private static BenchmarkChangeStoreResult NotFound(string id)
        => new() { Outcome = BenchmarkChangeStoreOutcome.NotFound, Error = $"Unknown experimental change '{id}'." };

    private static BenchmarkChangeStoreResult Conflict(string reason, string error)
        => new() { Outcome = BenchmarkChangeStoreOutcome.Conflict, ConflictReason = reason, Error = error };
}
