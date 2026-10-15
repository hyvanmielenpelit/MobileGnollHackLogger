namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MobileGnollHackLogger.Data;
using Overseer.Models;

public enum BenchmarkReportPackJobStatus { Running, Completed, CompletedWithErrors, Canceled, Failed }
public enum BenchmarkReportPackDocumentStatus { Pending, Writing, Repairing, Completed, CompletedWithWarnings, Failed, Canceled }

public class BenchmarkReportPackDocumentProgress
{
    public BenchmarkReportAudience Audience { get; set; }

    /// <summary>
    /// The document's subject: a model's entry key, or the comparison-scope subject key once the job
    /// has prepared it; empty for a completion job's document, whose subject is the job's.
    /// </summary>
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>The subject as the progress list names it: the model's label, or <c>Comparison #12</c>.</summary>
    public string SubjectLabel { get; set; } = string.Empty;
    public BenchmarkReportPackDocumentStatus Status { get; set; } = BenchmarkReportPackDocumentStatus.Pending;
    public long? DocumentId { get; set; }
    public string? ErrorMessage { get; set; }
    public int ModelCalls { get; set; }

    /// <summary>When the document first became <see cref="BenchmarkReportPackDocumentStatus.Writing"/>.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>When the document reached a terminal status: completed, failed or canceled.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }

    /// <summary>
    /// The first turn's validation issues, at most <see cref="BenchmarkReportPackJob.MaxDiagnosticIssues"/>; set
    /// when the revision starts, or when the document ends; null before.
    /// </summary>
    public List<BenchmarkReportValidationNote>? FirstTurnIssues { get; set; }

    /// <summary>The issues left after the last validation, at most <see cref="BenchmarkReportPackJob.MaxDiagnosticIssues"/>; null until the document ends.</summary>
    public List<BenchmarkReportValidationNote>? FinalIssues { get; set; }

    /// <summary>A Failed document's last raw reply, cut at <see cref="BenchmarkReportPackJob.MaxFailedReplyChars"/>; null otherwise.</summary>
    public string? FailedReply { get; set; }

    /// <summary>The step the document is at (<see cref="BenchmarkReportPackPhases"/>); empty while it waits.</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>A plain sentence on the current phase; null when there is none.</summary>
    public string? PhaseDetail { get; set; }

    /// <summary>Every phase the document reached, in order; kept in memory for diagnostics.</summary>
    public List<string> PhaseHistory { get; } = new();

    /// <summary>Whether the first draft passed every check; null until it has been checked.</summary>
    public bool? FirstDraftClean { get; set; }

    /// <summary>Every writer call of the document, in order.</summary>
    public List<BenchmarkReportPackCallDto> Calls { get; } = new();
}

public class BenchmarkReportPackJobLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
}

/// <summary>
/// In-memory state for one report-pack generation: an ordered list of (subject, document) rows — the
/// subjects of a model-scope job one after another, or the one comparison-scope subject — written one
/// after another. Documents are persisted as each completes; nothing is stored for a failed one.
/// </summary>
public class BenchmarkReportPackJob
{
    /// <summary>The validation issues a document's diagnostics keep per list.</summary>
    public const int MaxDiagnosticIssues = 80;

    /// <summary>The characters of a failed document's last reply the diagnostics keep.</summary>
    public const int MaxFailedReplyChars = 200_000;

    /// <summary>Ends a failed reply cut at <see cref="MaxFailedReplyChars"/>.</summary>
    public const string TruncatedMarker = "[truncated]";

    private readonly object _lock = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid PackId { get; set; } = Guid.NewGuid();

    /// <summary>The first subject's key; each document's own subject is on its progress row.</summary>
    public string SubjectKey { get; set; } = string.Empty;

    public string SubjectLabel { get; set; } = string.Empty;

    /// <summary>Per-model documents or comparison-scope documents.</summary>
    public BenchmarkReportScope Scope { get; set; } = BenchmarkReportScope.Model;

    /// <summary>The numbered comparison every document of a Report Pack job belongs to; null for a completion job.</summary>
    public int? ComparisonId { get; set; }
    public long? SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;

    /// <summary>The writer configuration, which <see cref="Overseer.Services.SystemConfigUsageGuard"/> protects while the job runs.</summary>
    public long WriterConfigId { get; set; }

    public string WriterDisplayName { get; set; } = string.Empty;

    /// <summary>The writer settings captured at job start; every document is written with them and records them.</summary>
    public long WriterSnapshotId { get; set; }

    public bool SameProviderAcknowledged { get; set; }

    public BenchmarkReportPackRequest Request { get; set; } = new();

    public string? StartedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public BenchmarkReportPackJobStatus Status { get; set; } = BenchmarkReportPackJobStatus.Running;

    public List<BenchmarkReportPackDocumentProgress> Documents { get; set; } = new();
    public List<BenchmarkReportPackJobLogEntry> Log { get; set; } = new();

    public int TotalModelCalls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }

    /// <summary>The SHA-256 of the writer's system prompt of the job's first document; null until it is built.</summary>
    public string? WriterPromptSha256 { get; set; }

    /// <summary>The writer's provider, model id and thinking level, from the configuration the job writes with; null until bound.</summary>
    public string? WriterProvider { get; set; }

    public string? WriterModelId { get; set; }
    public string? WriterThinkingLevel { get; set; }

    /// <summary>The comparison's reference model, as its label; null when it has none.</summary>
    public string? ReferenceModel { get; set; }

    /// <summary>The comparison's entry keys, e.g. <c>battery:13</c>; empty for a completion job.</summary>
    public List<string> Entries { get; set; } = new();

    /// <summary>The notes of the job's preparation (fact sheet and engine).</summary>
    public List<BenchmarkReportValidationNote> PreparationNotes { get; } = new();

    public CancellationTokenSource Cts { get; set; } = null!;

    public void AddLog(string message, string severity = "info")
    {
        lock (_lock)
        {
            Log.Add(new BenchmarkReportPackJobLogEntry { TimestampUtc = DateTime.UtcNow, Message = message, Severity = severity });
            if (Log.Count > 100)
            {
                Log.RemoveRange(0, Log.Count - 100);
            }
        }
    }

    /// <summary>The status of the first document of <paramref name="audience"/>; for a job with one subject.</summary>
    public void SetDocumentStatus(BenchmarkReportAudience audience, BenchmarkReportPackDocumentStatus status,
        string? errorMessage = null, long? documentId = null)
    {
        BenchmarkReportPackDocumentProgress? document;
        lock (_lock)
        {
            document = Documents.FirstOrDefault(d => d.Audience == audience);
        }
        if (document != null) SetDocumentStatus(document, status, errorMessage, documentId);
    }

    /// <summary>The status of one document row; its start and completion times as it first reaches them.</summary>
    public void SetDocumentStatus(BenchmarkReportPackDocumentProgress document, BenchmarkReportPackDocumentStatus status,
        string? errorMessage = null, long? documentId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_lock)
        {
            document.Status = status;
            if (errorMessage != null) document.ErrorMessage = errorMessage;
            if (documentId != null) document.DocumentId = documentId;
            if (status == BenchmarkReportPackDocumentStatus.Writing && document.StartedAtUtc == null)
            {
                document.StartedAtUtc = DateTime.UtcNow;
            }
            if (status is BenchmarkReportPackDocumentStatus.Completed or BenchmarkReportPackDocumentStatus.CompletedWithWarnings
                or BenchmarkReportPackDocumentStatus.Failed or BenchmarkReportPackDocumentStatus.Canceled)
            {
                document.CompletedAtUtc ??= DateTime.UtcNow;
            }
        }
    }

    /// <summary>One model call's usage, counted on the job and on the first document of <paramref name="audience"/>.</summary>
    public void AddUsage(BenchmarkReportAudience audience, long inputTokens, long outputTokens, decimal? cost)
    {
        BenchmarkReportPackDocumentProgress? document;
        lock (_lock)
        {
            document = Documents.FirstOrDefault(d => d.Audience == audience);
        }
        AddUsage(document, inputTokens, outputTokens, cost);
    }

    /// <summary>One model call's usage, counted on the job and on <paramref name="document"/> when given.</summary>
    public void AddUsage(BenchmarkReportPackDocumentProgress? document, long inputTokens, long outputTokens, decimal? cost)
    {
        lock (_lock)
        {
            TotalModelCalls++;
            InputTokens += inputTokens;
            OutputTokens += outputTokens;
            if (cost != null) CostUsd = (CostUsd ?? 0m) + cost.Value;
            if (document != null)
            {
                document.ModelCalls++;
                document.InputTokens += inputTokens;
                document.OutputTokens += outputTokens;
                if (cost != null) document.CostUsd = (document.CostUsd ?? 0m) + cost.Value;
            }
        }
    }

    /// <summary>
    /// A document's validation record once it has ended: its first-turn and final issues, each cut at
    /// <see cref="MaxDiagnosticIssues"/>, and the last raw reply of a failed one, cut at
    /// <see cref="MaxFailedReplyChars"/> with <see cref="TruncatedMarker"/>.
    /// </summary>
    public void SetDocumentDiagnostics(BenchmarkReportPackDocumentProgress document,
        IEnumerable<BenchmarkReportValidationNote>? firstTurnIssues, IEnumerable<BenchmarkReportValidationNote>? finalIssues,
        string? failedReply = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var first = CopyIssues(firstTurnIssues);
        var final = CopyIssues(finalIssues);
        string? reply = failedReply == null || failedReply.Length <= MaxFailedReplyChars
            ? failedReply
            : failedReply[..MaxFailedReplyChars] + TruncatedMarker;
        lock (_lock)
        {
            document.FirstTurnIssues = first;
            document.FinalIssues = final;
            document.FailedReply = reply;
        }
    }

    /// <summary>
    /// A document's first-turn issues while it is revised, each cut at <see cref="MaxDiagnosticIssues"/>: they
    /// travel on a running job's polls once the document is <see cref="BenchmarkReportPackPhases.Revising"/> or later.
    /// </summary>
    public void SetFirstTurnIssues(BenchmarkReportPackDocumentProgress document, IEnumerable<BenchmarkReportValidationNote>? issues)
    {
        ArgumentNullException.ThrowIfNull(document);
        var first = CopyIssues(issues);
        lock (_lock)
        {
            document.FirstTurnIssues = first;
        }
    }

    /// <summary>The step a document is at, with its plain sentence; each phase is added to its history.</summary>
    public void SetDocumentPhase(BenchmarkReportPackDocumentProgress document, string phase, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_lock)
        {
            document.Phase = phase;
            document.PhaseDetail = detail;
            document.PhaseHistory.Add(phase);
        }
    }

    /// <summary>Whether a document's first draft passed every check.</summary>
    public void SetFirstDraftClean(BenchmarkReportPackDocumentProgress document, bool clean)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_lock)
        {
            document.FirstDraftClean = clean;
        }
    }

    /// <summary>One writer call of a document, in call order; its usage is counted by <see cref="AddUsage(BenchmarkReportPackDocumentProgress?, long, long, decimal?)"/>.</summary>
    public void RecordCall(BenchmarkReportPackDocumentProgress document, BenchmarkReportPackCallDto call)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(call);
        lock (_lock)
        {
            document.Calls.Add(call);
        }
    }

    /// <summary>The notes a subject's preparation recorded, kept once each for the job's diagnostics.</summary>
    public void AddPreparationNotes(IEnumerable<BenchmarkReportValidationNote>? notes)
    {
        var copies = CopyIssues(notes, int.MaxValue);
        lock (_lock)
        {
            foreach (var note in copies)
            {
                if (!PreparationNotes.Any(n => n.Rule == note.Rule && n.Location == note.Location && n.Message == note.Message))
                {
                    PreparationNotes.Add(note);
                }
            }
        }
    }

    private static List<BenchmarkReportValidationNote> CopyIssues(IEnumerable<BenchmarkReportValidationNote>? issues, int max = MaxDiagnosticIssues)
        => (issues ?? Enumerable.Empty<BenchmarkReportValidationNote>())
            .Take(max)
            .Select(n => new BenchmarkReportValidationNote { Rule = n.Rule, Location = n.Location, Message = n.Message, Dropped = n.Dropped, Text = n.Text })
            .ToList();

    private static List<BenchmarkReportPackValidationIssueDto>? IssueDtos(List<BenchmarkReportValidationNote>? issues, BenchmarkReportAudience audience)
        => issues?.Select(n => BenchmarkReportRuleCatalog.ToIssueDto(n, BenchmarkReportRuleCatalog.SlotTitlesFor(audience))).ToList();

    /// <summary>The phases from <see cref="BenchmarkReportPackPhases.Revising"/> on, when the first draft's issues are known.</summary>
    private static readonly HashSet<string> RevisingOrLater = new(StringComparer.Ordinal)
    {
        BenchmarkReportPackPhases.Revising, BenchmarkReportPackPhases.CheckingRevision, BenchmarkReportPackPhases.Storing,
        BenchmarkReportPackPhases.DrawingCharts, BenchmarkReportPackPhases.Done
    };

    private static BenchmarkReportPackCallDto CopyCall(BenchmarkReportPackCallDto call) => new()
    {
        Kind = call.Kind,
        StartedAtUtc = call.StartedAtUtc,
        DurationMs = call.DurationMs,
        InputTokens = call.InputTokens,
        CachedInputTokens = call.CachedInputTokens,
        OutputTokens = call.OutputTokens,
        CostUsd = call.CostUsd,
        FinishReason = call.FinishReason,
        RequestMessages = call.RequestMessages,
        RequestCharacters = call.RequestCharacters
    };

    public void SetStatus(BenchmarkReportPackJobStatus status)
    {
        lock (_lock)
        {
            Status = status;
            if (status != BenchmarkReportPackJobStatus.Running && CompletedAtUtc == null)
            {
                CompletedAtUtc = DateTime.UtcNow;
            }
        }
    }

    public BenchmarkReportPackJobDto ToDto()
    {
        lock (_lock)
        {
            // The validation detail travels once the job has ended; on a running job's polls, only the
            // first-turn issues of a document under revision or past it.
            bool terminal = Status != BenchmarkReportPackJobStatus.Running;
            return new BenchmarkReportPackJobDto
            {
                Id = Id,
                PackId = PackId,
                SubjectKey = SubjectKey,
                SubjectLabel = SubjectLabel,
                Scope = Scope,
                ComparisonId = ComparisonId,
                SuiteId = SuiteId,
                SuiteName = SuiteName,
                WriterConfigId = WriterConfigId,
                WriterDisplayName = WriterDisplayName,
                StartedByUserId = StartedByUserId,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Status = Status.ToString(),
                TotalModelCalls = TotalModelCalls,
                InputTokens = InputTokens,
                OutputTokens = OutputTokens,
                CostUsd = CostUsd == null ? null : (double)CostUsd.Value,
                Documents = Documents.Select(d => new BenchmarkReportPackDocumentProgressDto
                {
                    Audience = d.Audience,
                    SubjectKey = d.SubjectKey,
                    SubjectLabel = d.SubjectLabel,
                    Status = d.Status.ToString(),
                    DocumentId = d.DocumentId,
                    ErrorMessage = d.ErrorMessage,
                    ModelCalls = d.ModelCalls,
                    StartedAtUtc = d.StartedAtUtc,
                    CompletedAtUtc = d.CompletedAtUtc,
                    InputTokens = d.InputTokens,
                    OutputTokens = d.OutputTokens,
                    CostUsd = d.CostUsd == null ? null : (double)d.CostUsd.Value,
                    FirstTurnIssues = terminal || RevisingOrLater.Contains(d.Phase) ? IssueDtos(d.FirstTurnIssues, d.Audience) : null,
                    FinalIssues = terminal ? IssueDtos(d.FinalIssues, d.Audience) : null,
                    FailedReply = terminal ? d.FailedReply : null,
                    Phase = d.Phase,
                    PhaseDetail = d.PhaseDetail,
                    FirstDraftClean = d.FirstDraftClean,
                    Calls = d.Calls.Select(CopyCall).ToList()
                }).ToList(),
                Log = Log.Select(l => new BenchmarkReportPackJobLogEntryDto
                {
                    TimestampUtc = l.TimestampUtc,
                    Message = l.Message,
                    Severity = l.Severity
                }).ToList(),
                ServerTimeUtc = DateTime.UtcNow,
                ReportFormatVersion = BenchmarkReportPackRenderer.CurrentFormatVersion(Scope),
                EngineVersion = Documents.Any(d => d.Audience == BenchmarkReportAudience.RootCauseAnalysis)
                    ? RootCause.RootCauseEngine.Version
                    : null,
                PdfLayoutVersion = Pdf.BenchmarkPdfRenderer.LayoutVersion,
                WordLayoutVersion = Word.BenchmarkWordRenderer.LayoutVersion,
                WriterPromptSha256 = WriterPromptSha256,
                WriterProvider = WriterProvider,
                WriterModelId = WriterModelId,
                WriterThinkingLevel = WriterThinkingLevel,
                ReferenceModel = ReferenceModel,
                Entries = Entries.ToList(),
                PreparationNotes = PreparationNotes.Select(n =>
                {
                    var issue = BenchmarkReportRuleCatalog.ToIssueDto(n);
                    issue.Severity = BenchmarkReportRuleCatalog.WarningSeverity;
                    return issue;
                }).ToList()
            };
        }
    }
}
