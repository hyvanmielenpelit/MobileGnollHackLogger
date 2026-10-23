namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A Markdown export of feedback issues. Overseer takes <see cref="IssueIds"/>, GnollBench <see cref="Fingerprints"/>;
/// at most 200 issues. <see cref="OccurrencesPerIssue"/> is 5, 10 (the default) or 25.
/// </summary>
public sealed class AdminFeedbackExportRequestDto
{
    /// <summary><c>overseer</c> or <c>gnollbench</c>.</summary>
    public string? Source { get; set; }
    public List<long>? IssueIds { get; set; }
    public List<string>? Fingerprints { get; set; }
    public int? OccurrencesPerIssue { get; set; }

    /// <summary>GnollBench only: include each occurrence's question text. Null reads true.</summary>
    public bool? IncludeQuestionText { get; set; }
}

/// <summary>The export file, and the requested issues that no longer exist.</summary>
public sealed class AdminFeedbackExportResultDto
{
    public string FileName { get; set; } = "";
    public string Markdown { get; set; } = "";
    public int IssueCount { get; set; }

    /// <summary>Overseer issue ids that no longer exist.</summary>
    public List<long> MissingIds { get; set; } = new();

    /// <summary>GnollBench fingerprints that no self-report has any more, normalized.</summary>
    public List<string> MissingFingerprints { get; set; } = new();
}

/// <summary>
/// One issue of a resolution file (<c>overseer-feedback-resolutions</c> version 1). For each of <see cref="Status"/>,
/// <see cref="Resolution"/>, <see cref="FixedInVersion"/> and <see cref="Note"/>, absent or null keeps the stored
/// value and an empty or blank string clears it. <see cref="ExpectedStatus"/> and <see cref="ExpectedTriagedUtc"/> are
/// the current values the preview showed; apply requires the status and compares both with the stored ones.
/// </summary>
public sealed class AdminFeedbackResolutionItemDto
{
    /// <summary>Overseer only: the issue id.</summary>
    public long? Id { get; set; }

    /// <summary>64 lowercase hex characters.</summary>
    public string? Fingerprint { get; set; }

    /// <summary><c>new</c>, <c>acknowledged</c>, <c>resolved</c> or <c>dismissed</c>.</summary>
    public string? Status { get; set; }
    public string? Resolution { get; set; }
    public string? FixedInVersion { get; set; }

    /// <summary>The triage note.</summary>
    public string? Note { get; set; }

    /// <summary>Apply only: the status the preview showed, <c>regressed</c> included.</summary>
    public string? ExpectedStatus { get; set; }

    /// <summary>Apply only: the triage time the preview showed; null when the issue was never triaged.</summary>
    public DateTime? ExpectedTriagedUtc { get; set; }

    /// <summary>Keys the format does not define; any is an error.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }
}

/// <summary>
/// A resolution file as JSON: the body of both preview and apply. <see cref="Format"/> and <see cref="Version"/> may
/// be left out; when given they must be <c>overseer-feedback-resolutions</c> and 1.
/// </summary>
public sealed class AdminFeedbackResolutionRequestDto
{
    public string? Format { get; set; }
    public int? Version { get; set; }

    /// <summary><c>overseer</c> or <c>gnollbench</c>.</summary>
    public string? Source { get; set; }

    /// <summary>The export's time; optional, it enables change detection.</summary>
    public DateTime? ExportedAtUtc { get; set; }
    public List<AdminFeedbackResolutionItemDto>? Items { get; set; }

    /// <summary>Keys the format does not define; any is an error.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }
}

/// <summary>One validation error; <see cref="Index"/> is the item's zero-based position, null for the file itself.</summary>
public sealed class AdminFeedbackResolutionErrorDto
{
    public int? Index { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>The 400 body of a file that breaks the format's rules.</summary>
public sealed class AdminFeedbackResolutionInvalidDto
{
    public string Message { get; set; } = "";
    public List<AdminFeedbackResolutionErrorDto> Errors { get; set; } = new();
}

/// <summary>An issue's triage values as stored now (<see cref="Status"/> is the shown status, <c>regressed</c> included).</summary>
public sealed class AdminFeedbackResolutionStateDto
{
    public string Status { get; set; } = "new";
    public string? Note { get; set; }
    public string? Resolution { get; set; }
    public string? FixedInVersion { get; set; }
    public DateTime? TriagedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}

/// <summary>An issue's triage values once the item is applied.</summary>
public sealed class AdminFeedbackResolutionValuesDto
{
    public string Status { get; set; } = "new";
    public string? Note { get; set; }
    public string? Resolution { get; set; }
    public string? FixedInVersion { get; set; }
}

/// <summary>Which values the item changes.</summary>
public sealed class AdminFeedbackResolutionChangesDto
{
    public bool Status { get; set; }
    public bool Note { get; set; }
    public bool Resolution { get; set; }
    public bool FixedInVersion { get; set; }
}

/// <summary>
/// One item of a preview. <see cref="Errors"/> is non-empty when the issue is gone or its fingerprint does not belong
/// to the id; such an item cannot be applied. <see cref="OccurrencesSinceExport"/> is null without an export time.
/// </summary>
public sealed class AdminFeedbackResolutionPreviewItemDto
{
    public int Index { get; set; }
    public long? Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public bool Exists { get; set; }
    public bool FingerprintMatches { get; set; }

    /// <summary>The issue's category wire value; null when it does not exist.</summary>
    public string? Category { get; set; }

    /// <summary>The issue's subject, model-written text to show as plain text; null when it does not exist.</summary>
    public string? Subject { get; set; }
    public AdminFeedbackResolutionStateDto? Current { get; set; }
    public AdminFeedbackResolutionValuesDto? After { get; set; }
    public AdminFeedbackResolutionChangesDto Changes { get; set; } = new();
    public bool HasChanges { get; set; }
    public int? OccurrencesSinceExport { get; set; }
    public bool TriagedSinceExport { get; set; }
    public bool RegressedSinceExport { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Notices { get; set; } = new();
}

/// <summary>The preview of a resolution file: the file's notices and one entry per item, in the file's order.</summary>
public sealed class AdminFeedbackResolutionPreviewDto
{
    public string Source { get; set; } = "";
    public DateTime? ExportedAtUtc { get; set; }
    public int ErrorCount { get; set; }
    public int ChangeCount { get; set; }
    public int TriagedSinceExportCount { get; set; }
    public List<string> Notices { get; set; } = new();
    public List<AdminFeedbackResolutionPreviewItemDto> Items { get; set; } = new();
}

/// <summary>The result of an apply: items written and items that changed nothing.</summary>
public sealed class AdminFeedbackResolutionApplyResultDto
{
    public int Changed { get; set; }
    public int Unchanged { get; set; }
}

/// <summary>An issue that changed after the preview. <see cref="Reason"/> is <c>changed</c> or <c>missing</c>.</summary>
public sealed class AdminFeedbackResolutionConflictItemDto
{
    public int Index { get; set; }
    public long? Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Reason { get; set; } = "changed";
    public string? ExpectedStatus { get; set; }
    public string? CurrentStatus { get; set; }
    public DateTime? ExpectedTriagedUtc { get; set; }
    public DateTime? CurrentTriagedUtc { get; set; }
}

/// <summary>The 409 body of an apply that found issues changed since the preview; nothing was written.</summary>
public sealed class AdminFeedbackResolutionConflictDto
{
    public string Message { get; set; } = "";
    public List<AdminFeedbackResolutionConflictItemDto> Conflicts { get; set; } = new();
}
