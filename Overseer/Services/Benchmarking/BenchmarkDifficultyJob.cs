namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Overseer.Models;

public enum BenchmarkDifficultyJobStatus { Running, Completed, CompletedWithErrors, Cancelled, Failed }
public enum BenchmarkDifficultyItemStatus { Pending, Assessing, Rated, Failed, Skipped }

public class BenchmarkDifficultyJobItem
{
    public long QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public string QuestionTextExcerpt { get; set; } = string.Empty;
    public BenchmarkDifficultyItemStatus Status { get; set; } = BenchmarkDifficultyItemStatus.Pending;
    public int? Difficulty { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BenchmarkDifficultyJobLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

public class BenchmarkDifficultyJob
{
    private readonly object _lock = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public string Scope { get; set; } = "suite";
    public long AssessorConfigId { get; set; }
    public string AssessorDisplayName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public BenchmarkDifficultyJobStatus Status { get; set; } = BenchmarkDifficultyJobStatus.Running;

    public List<BenchmarkDifficultyJobItem> Items { get; set; } = new();
    public List<BenchmarkDifficultyJobLogEntry> Log { get; set; } = new();

    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }

    public CancellationTokenSource Cts { get; set; } = null!;

    public void AddLog(string message, string severity = "info", string? rawExcerpt = null)
    {
        lock (_lock)
        {
            Log.Add(new BenchmarkDifficultyJobLogEntry
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

    public void UpdateItemsStatus(IEnumerable<long> questionIds, BenchmarkDifficultyItemStatus status)
    {
        lock (_lock)
        {
            var set = new HashSet<long>(questionIds);
            foreach (var item in Items)
            {
                if (set.Contains(item.QuestionId))
                {
                    item.Status = status;
                }
            }
        }
    }

    public void SetItemRated(long questionId, int difficulty)
    {
        lock (_lock)
        {
            var item = Items.FirstOrDefault(i => i.QuestionId == questionId);
            if (item != null)
            {
                item.Status = BenchmarkDifficultyItemStatus.Rated;
                item.Difficulty = difficulty;
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
                item.Status = BenchmarkDifficultyItemStatus.Failed;
                item.ErrorMessage = errorMessage;
            }
        }
    }

    public void MarkRemainingSkipped()
    {
        lock (_lock)
        {
            foreach (var item in Items)
            {
                if (item.Status == BenchmarkDifficultyItemStatus.Pending || item.Status == BenchmarkDifficultyItemStatus.Assessing)
                {
                    item.Status = BenchmarkDifficultyItemStatus.Skipped;
                }
            }
        }
    }

    public void RecordModelCall(int promptTokens, int outputTokens)
    {
        lock (_lock)
        {
            TotalModelCalls++;
            PromptTokens += promptTokens;
            OutputTokens += outputTokens;
        }
    }

    public void SetStatus(BenchmarkDifficultyJobStatus status)
    {
        lock (_lock)
        {
            Status = status;
            if (status != BenchmarkDifficultyJobStatus.Running && CompletedAtUtc == null)
            {
                CompletedAtUtc = DateTime.UtcNow;
            }
        }
    }

    public DifficultyAssessmentJobDto ToDto()
    {
        lock (_lock)
        {
            int rated = Items.Count(i => i.Status == BenchmarkDifficultyItemStatus.Rated);
            int failed = Items.Count(i => i.Status == BenchmarkDifficultyItemStatus.Failed);
            int total = Items.Count;

            return new DifficultyAssessmentJobDto
            {
                Id = Id,
                SuiteId = SuiteId,
                SuiteName = SuiteName,
                Scope = Scope,
                AssessorConfigId = AssessorConfigId,
                AssessorDisplayName = AssessorDisplayName,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Status = Status.ToString(),
                RatedCount = rated,
                FailedCount = failed,
                TotalCount = total,
                TotalModelCalls = TotalModelCalls,
                PromptTokens = PromptTokens,
                OutputTokens = OutputTokens,
                Items = Items.Select(i => new DifficultyAssessmentJobItemDto
                {
                    QuestionId = i.QuestionId,
                    OrderIndex = i.OrderIndex,
                    QuestionTextExcerpt = i.QuestionTextExcerpt,
                    Status = i.Status.ToString(),
                    Difficulty = i.Difficulty,
                    ErrorMessage = i.ErrorMessage
                }).ToList(),
                Log = Log.Select(l => new DifficultyAssessmentJobLogEntryDto
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
