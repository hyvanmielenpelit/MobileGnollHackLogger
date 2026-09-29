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
}

public class BenchmarkReportPackJobLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
}

/// <summary>
/// In-memory state for one report-pack generation: one subject, its documents written one after
/// another. Documents are persisted as each completes; nothing is stored for a failed one.
/// </summary>
public class BenchmarkReportPackJob
{
    private readonly object _lock = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid PackId { get; set; } = Guid.NewGuid();
    public string SubjectKey { get; set; } = string.Empty;
    public string SubjectLabel { get; set; } = string.Empty;
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

    public void SetDocumentStatus(BenchmarkReportAudience audience, BenchmarkReportPackDocumentStatus status,
        string? errorMessage = null, long? documentId = null)
    {
        lock (_lock)
        {
            var document = Documents.FirstOrDefault(d => d.Audience == audience);
            if (document == null) return;
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

    public void AddUsage(BenchmarkReportAudience audience, long inputTokens, long outputTokens, decimal? cost)
    {
        lock (_lock)
        {
            TotalModelCalls++;
            InputTokens += inputTokens;
            OutputTokens += outputTokens;
            if (cost != null) CostUsd = (CostUsd ?? 0m) + cost.Value;
            var document = Documents.FirstOrDefault(d => d.Audience == audience);
            if (document != null)
            {
                document.ModelCalls++;
                document.InputTokens += inputTokens;
                document.OutputTokens += outputTokens;
                if (cost != null) document.CostUsd = (document.CostUsd ?? 0m) + cost.Value;
            }
        }
    }

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
            return new BenchmarkReportPackJobDto
            {
                Id = Id,
                PackId = PackId,
                SubjectKey = SubjectKey,
                SubjectLabel = SubjectLabel,
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
                    Status = d.Status.ToString(),
                    DocumentId = d.DocumentId,
                    ErrorMessage = d.ErrorMessage,
                    ModelCalls = d.ModelCalls,
                    StartedAtUtc = d.StartedAtUtc,
                    CompletedAtUtc = d.CompletedAtUtc,
                    InputTokens = d.InputTokens,
                    OutputTokens = d.OutputTokens,
                    CostUsd = d.CostUsd == null ? null : (double)d.CostUsd.Value
                }).ToList(),
                Log = Log.Select(l => new BenchmarkReportPackJobLogEntryDto
                {
                    TimestampUtc = l.TimestampUtc,
                    Message = l.Message,
                    Severity = l.Severity
                }).ToList()
            };
        }
    }
}
