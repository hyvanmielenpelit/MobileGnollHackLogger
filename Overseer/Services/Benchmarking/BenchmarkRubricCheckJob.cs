namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Overseer.Models;

public enum BenchmarkRubricCheckJobStatus { Running, Completed, CompletedWithErrors, Cancelled, Failed }
public enum BenchmarkRubricCheckItemStatus { Pending, Checking, Completed, Failed, Skipped }

public class RubricCheckFinding
{
    public string Claim { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string? BoardQuote { get; set; }
}

public class BenchmarkRubricCheckJobItem
{
    public long QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public string QuestionTextExcerpt { get; set; } = string.Empty;
    public BenchmarkRubricCheckItemStatus Status { get; set; } = BenchmarkRubricCheckItemStatus.Pending;
    public string? Verdict { get; set; }
    public List<RubricCheckFinding> Findings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class BenchmarkRubricCheckJobLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

public class BenchmarkRubricCheckJob
{
    private readonly object _lock = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public string Scope { get; set; } = "suite";
    public long CheckerConfigId { get; set; }
    public string CheckerDisplayName { get; set; } = string.Empty;

    public string? StartedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public BenchmarkRubricCheckJobStatus Status { get; set; } = BenchmarkRubricCheckJobStatus.Running;

    public List<BenchmarkRubricCheckJobItem> Items { get; set; } = new();
    public List<BenchmarkRubricCheckJobLogEntry> Log { get; set; } = new();

    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }

    public CancellationTokenSource Cts { get; set; } = null!;

    public void AddLog(string message, string severity = "info", string? rawExcerpt = null)
    {
        lock (_lock)
        {
            Log.Add(new BenchmarkRubricCheckJobLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Message = message,
                Severity = severity,
                RawExcerpt = rawExcerpt
            });

            if (Log.Count > 100)
            {
                Log.RemoveRange(0, Log.Count - 100);
            }
        }
    }

    public void SetItemResult(long questionId, string verdict, List<RubricCheckFinding> findings)
    {
        lock (_lock)
        {
            var item = Items.FirstOrDefault(i => i.QuestionId == questionId);
            if (item != null)
            {
                item.Status = BenchmarkRubricCheckItemStatus.Completed;
                item.Verdict = verdict;
                item.Findings = findings;
                item.ErrorMessage = null;
            }
        }
    }

    public void SetItemFailed(long questionId, string errorMessage)
    {
        lock (_lock)
        {
            var item = Items.FirstOrDefault(i => i.QuestionId == questionId);
            if (item != null)
            {
                item.Status = BenchmarkRubricCheckItemStatus.Failed;
                item.ErrorMessage = errorMessage;
            }
        }
    }

    public void AddUsage(int promptTokens, int outputTokens, int modelCalls = 1)
    {
        lock (_lock)
        {
            PromptTokens += promptTokens;
            OutputTokens += outputTokens;
            TotalModelCalls += modelCalls;
        }
    }

    public void SetStatus(BenchmarkRubricCheckJobStatus status)
    {
        lock (_lock)
        {
            Status = status;
            if (status != BenchmarkRubricCheckJobStatus.Running && CompletedAtUtc == null)
            {
                CompletedAtUtc = DateTime.UtcNow;
            }
        }
    }

    public RubricCheckJobDto ToDto()
    {
        lock (_lock)
        {
            return new RubricCheckJobDto
            {
                Id = Id,
                SuiteId = SuiteId,
                SuiteName = SuiteName,
                Scope = Scope,
                CheckerConfigId = CheckerConfigId,
                CheckerDisplayName = CheckerDisplayName,
                StartedByUserId = StartedByUserId,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Status = Status.ToString(),
                TotalModelCalls = TotalModelCalls,
                PromptTokens = PromptTokens,
                OutputTokens = OutputTokens,
                Items = Items.Select(i => new RubricCheckJobItemDto
                {
                    QuestionId = i.QuestionId,
                    OrderIndex = i.OrderIndex,
                    QuestionTextExcerpt = i.QuestionTextExcerpt,
                    Status = i.Status.ToString(),
                    Verdict = i.Verdict,
                    Findings = i.Findings.Select(f => new RubricCheckFindingDto
                    {
                        Claim = f.Claim,
                        Assessment = f.Assessment,
                        BoardQuote = f.BoardQuote
                    }).ToList(),
                    ErrorMessage = i.ErrorMessage
                }).ToList(),
                Log = Log.Select(l => new RubricCheckJobLogEntryDto
                {
                    TimestampUtc = l.TimestampUtc,
                    Message = l.Message,
                    Severity = l.Severity,
                    RawExcerpt = l.RawExcerpt
                }).ToList()
            };
        }
    }
}
