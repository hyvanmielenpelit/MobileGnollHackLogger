namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MobileGnollHackLogger.Data;
using Overseer.Models;

public enum BenchmarkGenerationJobStatus { Running, Completed, CompletedWithErrors, Cancelled, Failed }
public enum BenchmarkGenerationItemStatus { Pending, Generating, Completed, Failed, Skipped }

public class BenchmarkGenerationJobItem
{
    public BenchmarkDifficulty Difficulty { get; set; }
    public int RequestedCount { get; set; }
    public int GeneratedCount { get; set; }
    public BenchmarkGenerationItemStatus Status { get; set; } = BenchmarkGenerationItemStatus.Pending;
    public string? ErrorMessage { get; set; }
}

public class BenchmarkGenerationJobLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

public class BenchmarkGenerationJob
{
    private readonly object _lock = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public long GeneratorConfigId { get; set; }
    public string GeneratorDisplayName { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;

    public string? StartedByUserId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public BenchmarkGenerationJobStatus Status { get; set; } = BenchmarkGenerationJobStatus.Running;

    public List<BenchmarkGenerationJobItem> Items { get; set; } = new();
    public List<BenchmarkGenerationJobLogEntry> Log { get; set; } = new();

    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }

    public CancellationTokenSource Cts { get; set; } = null!;

    public void AddLog(string message, string severity = "info", string? rawExcerpt = null)
    {
        lock (_lock)
        {
            Log.Add(new BenchmarkGenerationJobLogEntry
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

    public void SetItemStatus(BenchmarkDifficulty difficulty, BenchmarkGenerationItemStatus status, string? errorMessage = null, int? generatedCount = null)
    {
        lock (_lock)
        {
            var item = Items.FirstOrDefault(i => i.Difficulty == difficulty);
            if (item != null)
            {
                item.Status = status;
                if (errorMessage != null) item.ErrorMessage = errorMessage;
                if (generatedCount.HasValue) item.GeneratedCount = generatedCount.Value;
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

    public void SetStatus(BenchmarkGenerationJobStatus status)
    {
        lock (_lock)
        {
            Status = status;
            if (status != BenchmarkGenerationJobStatus.Running && CompletedAtUtc == null)
            {
                CompletedAtUtc = DateTime.UtcNow;
            }
        }
    }

    public QuestionGenerationJobDto ToDto()
    {
        lock (_lock)
        {
            return new QuestionGenerationJobDto
            {
                Id = Id,
                SuiteId = SuiteId,
                SuiteName = SuiteName,
                GeneratorConfigId = GeneratorConfigId,
                GeneratorDisplayName = GeneratorDisplayName,
                StartedByUserId = StartedByUserId,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Status = Status.ToString(),
                TotalModelCalls = TotalModelCalls,
                PromptTokens = PromptTokens,
                OutputTokens = OutputTokens,
                Items = Items.Select(i => new QuestionGenerationJobItemDto
                {
                    Difficulty = (int)i.Difficulty,
                    DifficultyName = i.Difficulty.ToString(),
                    RequestedCount = i.RequestedCount,
                    GeneratedCount = i.GeneratedCount,
                    Status = i.Status.ToString(),
                    ErrorMessage = i.ErrorMessage
                }).ToList(),
                Log = Log.Select(l => new QuestionGenerationJobLogEntryDto
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
