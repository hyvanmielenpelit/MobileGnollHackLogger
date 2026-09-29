namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>The reader a report-pack document is written for.</summary>
public enum BenchmarkReportAudience
{
    ExecutiveSummary = 1,
    TechnicalReport = 2,
    InternalBrief = 3,
}

public enum BenchmarkReportDocumentStatus
{
    Completed = 1,

    /// <summary>The validator dropped items or paragraphs, or kept one it flagged, after the repair turn; the validation notes list them.</summary>
    CompletedWithWarnings = 2,
}

/// <summary>How a report-pack document came to be written.</summary>
public enum BenchmarkReportDocumentOrigin
{
    /// <summary>A report pack started from Model Comparison, about one entry among its peers.</summary>
    ReportPack = 1,

    /// <summary>Written once after a run completed, about that run on its own, with no peers.</summary>
    RunCompletion = 2,
}

/// <summary>
/// Where a run's two AI-written run-completion documents stand. Stored on the run as
/// <c>ReportDocumentsStatus</c>; the documents themselves are <see cref="BenchmarkReportDocument"/> rows.
/// </summary>
public enum BenchmarkRunReportDocumentsStatus
{
    /// <summary>No writer was chosen, or nothing has been asked of it yet.</summary>
    NotRequested = 0,

    /// <summary>Waiting for the report-pack slot.</summary>
    Pending = 1,

    Writing = 2,

    Completed = 3,

    /// <summary>Both documents were stored, and at least one carries validation warnings.</summary>
    CompletedWithWarnings = 4,

    /// <summary>At least one document could not be written; any document that was stored is kept.</summary>
    Failed = 5,

    /// <summary>The compliance guard refused the spend; nothing was written.</summary>
    Skipped = 6,

    /// <summary>An administrator canceled the job; documents written before the cancellation are kept.</summary>
    Canceled = 7,
}

/// <summary>
/// One AI-written report-pack document: the writer's final, validated prose, the computed fact sheet
/// and the verbatim content snapshot it may print, all captured at generation.
///
/// <para>The row is immutable once written. Nothing rendered is stored: every download renders the
/// stored JSON at the requested disclosure level and peer naming, deterministically and without a
/// model call. There are no foreign keys to runs, so a deleted run leaves its documents intact.</para>
/// </summary>
public class BenchmarkReportDocument
{
    public long Id { get; set; }

    /// <summary>Documents generated together by one report-pack job share this id.</summary>
    public Guid PackId { get; set; }

    public BenchmarkReportAudience Audience { get; set; }

    /// <summary>A report pack's document, or one written after a run completed. Rows written before the column existed are report-pack documents.</summary>
    public BenchmarkReportDocumentOrigin Origin { get; set; } = BenchmarkReportDocumentOrigin.ReportPack;

    /// <summary>The comparison entry key of the subject: <c>run:&lt;id&gt;</c> or <c>group:&lt;id&gt;</c>.</summary>
    [MaxLength(64)]
    public string SubjectKey { get; set; } = default!;

    /// <summary>The subject's display label when the document was generated.</summary>
    [MaxLength(256)]
    public string SubjectLabel { get; set; } = default!;

    /// <summary>The subject's run ids, as a JSON array of longs in ascending order.</summary>
    public string SubjectRunIdsJson { get; set; } = default!;

    /// <summary>The model comparison request the peers were computed from, as JSON.</summary>
    public string ComparisonRequestJson { get; set; } = default!;

    /// <summary>
    /// Lower-case hex SHA-256 of the comparison's entry set (its sorted run and group ids); documents of
    /// one comparison share it whatever their subject or pricing basis. Null when it could not be derived.
    /// </summary>
    [MaxLength(64)]
    public string? ComparisonKey { get; set; }

    public long? SuiteId { get; set; }

    [MaxLength(256)]
    public string SuiteName { get; set; } = default!;

    public long? WriterConfigId { get; set; }

    public long? WriterModelSnapshotId { get; set; }
    public SystemAiConfigurationSnapshot? WriterModelSnapshot { get; set; }

    [MaxLength(256)]
    public string WriterDisplayName { get; set; } = default!;

    [MaxLength(64)]
    public string WriterProvider { get; set; } = default!;

    [MaxLength(128)]
    public string WriterModelId { get; set; } = default!;

    [MaxLength(32)]
    public string? WriterThinkingLevel { get; set; }

    /// <summary>The writer shares the subject's provider and the operator acknowledged the warning.</summary>
    public bool SameProviderAcknowledged { get; set; }

    /// <summary>The renderer's format version when the document was generated.</summary>
    public int ReportFormatVersion { get; set; }

    [MaxLength(64)]
    public string WriterPromptSha256 { get; set; } = default!;

    /// <summary>The length answer excerpts in <see cref="ContentJson"/> were cut to.</summary>
    public int AnswerExcerptChars { get; set; }

    public string FactsJson { get; set; } = default!;

    /// <summary>
    /// The verbatim material the renderer may print at Detailed and Full disclosure: question text as
    /// asked, rubric as graded, answer excerpts (with the complete answer when an excerpt was cut),
    /// grader evidence and verifier rulings, taken from the subject's answer rows at generation.
    /// </summary>
    public string ContentJson { get; set; } = default!;

    /// <summary>The writer's output after validation; dropped items are already removed.</summary>
    public string WriterOutputJson { get; set; } = default!;

    public string ValidationNotesJson { get; set; } = default!;

    [MaxLength(512)]
    public string Title { get; set; } = default!;

    public BenchmarkReportDocumentStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long DurationMs { get; set; }

    public decimal? CostUsd { get; set; }

    [MaxLength(32)]
    public string? PricingSource { get; set; }

    public List<BenchmarkReportDocumentRun> Runs { get; set; } = new();
}

/// <summary>
/// One run a report-pack document's subject or one of its peers covers, with that run's scoring
/// fingerprint at generation. Comparing the fingerprint with the run as it is now tells whether the
/// run was re-scored or re-run after the document was written.
/// </summary>
public class BenchmarkReportDocumentRun
{
    public long DocumentId { get; set; }
    public BenchmarkReportDocument Document { get; set; } = default!;

    /// <summary>A run id, deliberately without a foreign key: deleting the run keeps the document.</summary>
    public long RunId { get; set; }

    /// <summary>The run belongs to a peer, not to the subject. Documents written before peer rows were stored have none.</summary>
    public bool IsPeer { get; set; }

    public int? FinalScore { get; set; }
    public int? QualityIndex { get; set; }
    public int? SpeedIndex { get; set; }
    public int ScoringMethodVersion { get; set; }
    public DateTime? RerunCompletedAtUtc { get; set; }

    /// <summary>First 16 hex characters of SHA-256 over <c>AssessmentJson + "\n" + CoAssessorSynthesisJson</c>.</summary>
    [MaxLength(16)]
    public string SynthesisSha256 { get; set; } = default!;
}
