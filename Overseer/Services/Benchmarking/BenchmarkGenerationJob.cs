namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MobileGnollHackLogger.Data;
using Overseer.Models;

public enum BenchmarkGenerationJobStatus { Running, Completed, CompletedWithErrors, Cancelled, Failed }
public enum BenchmarkGenerationItemStatus { Pending, Generating, Repairing, Completed, Failed, Skipped }

/// <summary>What an item asks the generator for.</summary>
public enum BenchmarkGenerationItemKind
{
    /// <summary>A batch of new questions for one difficulty band.</summary>
    Band,
    /// <summary>A replacement rubric for one existing question; the question text is untouched.</summary>
    RubricOnly,
    /// <summary>A replacement question and rubric for one existing question.</summary>
    ReplaceQuestion
}

/// <summary>
/// The generator model chosen for a job, snapshotted from a <see cref="SystemAiApiConfiguration"/>
/// at dispatch time so a later configuration edit cannot change what an in-flight or completed job
/// reports it used.
/// </summary>
public record GeneratorSelection(
    long ConfigId,
    string DisplayName,
    string Provider,
    string ModelId,
    string? ThinkingLevel,
    string? ReasoningMode,
    string? ServiceTier);

public class BenchmarkGenerationJobItem
{
    public BenchmarkGenerationItemKind Kind { get; set; } = BenchmarkGenerationItemKind.Band;
    public BenchmarkDifficulty Difficulty { get; set; }
    public int RequestedCount { get; set; }
    public int GeneratedCount { get; set; }
    public BenchmarkGenerationItemStatus Status { get; set; } = BenchmarkGenerationItemStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>The question this item replaces or adds a rubric to; null for a <see cref="BenchmarkGenerationItemKind.Band"/> item.</summary>
    public long? TargetQuestionId { get; set; }
    public int? TargetQuestionOrderIndex { get; set; }
    /// <summary>The target's question text, truncated to 120 characters.</summary>
    public string? TargetQuestionExcerpt { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public int ModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>Questions this item created, in the order they were saved.</summary>
    public List<long> CreatedQuestionIds { get; set; } = new();
    /// <summary>Questions this item updated in place (rubric-only or replacement).</summary>
    public List<long> UpdatedQuestionIds { get; set; } = new();
    /// <summary>Previously generated questions this item removes before generating its replacements.</summary>
    public List<long> QuestionIdsToDiscard { get; set; } = new();
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
    public string? GeneratorProvider { get; set; }
    public string? GeneratorModelId { get; set; }
    public string? GeneratorThinkingLevel { get; set; }
    public string? GeneratorReasoningMode { get; set; }
    public string? GeneratorServiceTier { get; set; }
    public string Instructions { get; set; } = string.Empty;

    public long? GameSnapshotId { get; set; }
    public string? GameSnapshotName { get; set; }

    /// <summary>The job this one retried, when it is a retry; null otherwise.</summary>
    public string? RetryOfJobId { get; set; }
    /// <summary>"Generation" | "Retry" | "Regeneration".</summary>
    public string JobKind { get; set; } = "Generation";

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

    /// <summary>Mutates one item's status under the job lock; the runner never sets item fields directly.</summary>
    public void SetItemStatus(BenchmarkGenerationJobItem item, BenchmarkGenerationItemStatus status, string? errorMessage = null, int? generatedCount = null)
    {
        lock (_lock)
        {
            item.Status = status;
            if (errorMessage != null) item.ErrorMessage = errorMessage;
            if (generatedCount.HasValue) item.GeneratedCount = generatedCount.Value;
        }
    }

    /// <summary>Records one model call's usage against both the item and the job total, under the job lock.</summary>
    public void SetItemUsage(BenchmarkGenerationJobItem item, int promptTokens, int outputTokens, int modelCalls = 1)
    {
        lock (_lock)
        {
            item.PromptTokens += promptTokens;
            item.OutputTokens += outputTokens;
            item.ModelCalls += modelCalls;

            PromptTokens += promptTokens;
            OutputTokens += outputTokens;
            TotalModelCalls += modelCalls;
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

    /// <summary>
    /// Builds the job for a retry of failed or partial bands. Per selected band, the requested
    /// count is the full previous count when discarding, or only the shortfall otherwise; a band
    /// not selected carries over as <see cref="BenchmarkGenerationItemStatus.Skipped"/> with no
    /// count. Throws when every selected band's requested count comes out at zero.
    /// </summary>
    public static BenchmarkGenerationJob CreateRetry(
        BenchmarkGenerationJob previous,
        IReadOnlyCollection<BenchmarkDifficulty> bands,
        bool discardExisting,
        GeneratorSelection generator,
        string instructions,
        string? startedByUserId)
    {
        var selected = new HashSet<BenchmarkDifficulty>(bands);
        var items = new List<BenchmarkGenerationJobItem>();

        foreach (var prevItem in previous.Items.Where(i => i.Kind == BenchmarkGenerationItemKind.Band))
        {
            if (!selected.Contains(prevItem.Difficulty))
            {
                items.Add(new BenchmarkGenerationJobItem
                {
                    Difficulty = prevItem.Difficulty,
                    RequestedCount = 0,
                    Status = BenchmarkGenerationItemStatus.Skipped
                });
                continue;
            }

            int requested = discardExisting
                ? prevItem.RequestedCount
                : Math.Max(0, prevItem.RequestedCount - prevItem.GeneratedCount);

            items.Add(new BenchmarkGenerationJobItem
            {
                Difficulty = prevItem.Difficulty,
                RequestedCount = requested,
                Status = requested > 0 ? BenchmarkGenerationItemStatus.Pending : BenchmarkGenerationItemStatus.Skipped,
                QuestionIdsToDiscard = discardExisting ? prevItem.CreatedQuestionIds.ToList() : new List<long>()
            });
        }

        if (items.Sum(i => i.RequestedCount) == 0)
        {
            throw new ArgumentException("No bands were requested for retry, or every requested band already has its full count.");
        }

        return new BenchmarkGenerationJob
        {
            SuiteId = previous.SuiteId,
            SuiteName = previous.SuiteName,
            GeneratorConfigId = generator.ConfigId,
            GeneratorDisplayName = generator.DisplayName,
            GeneratorProvider = generator.Provider,
            GeneratorModelId = generator.ModelId,
            GeneratorThinkingLevel = generator.ThinkingLevel,
            GeneratorReasoningMode = generator.ReasoningMode,
            GeneratorServiceTier = generator.ServiceTier,
            GameSnapshotId = previous.GameSnapshotId,
            GameSnapshotName = previous.GameSnapshotName,
            Instructions = instructions,
            StartedByUserId = startedByUserId,
            RetryOfJobId = previous.Id,
            JobKind = "Retry",
            Items = items
        };
    }

    /// <summary>
    /// Builds the job for a per-question regeneration: one <c>Pending</c> item per target,
    /// carrying the target's own difficulty. Throws on an empty target list.
    /// </summary>
    public static BenchmarkGenerationJob CreateRegeneration(
        long suiteId,
        string suiteName,
        long? snapshotId,
        string? snapshotName,
        IReadOnlyList<(long Id, int OrderIndex, BenchmarkDifficulty Difficulty, string Text)> targets,
        BenchmarkGenerationItemKind kind,
        GeneratorSelection generator,
        string instructions,
        string? startedByUserId)
    {
        if (targets == null || targets.Count == 0)
        {
            throw new ArgumentException("At least one question must be selected to regenerate.");
        }

        var items = targets.Select(t => new BenchmarkGenerationJobItem
        {
            Kind = kind,
            Difficulty = t.Difficulty,
            RequestedCount = 1,
            Status = BenchmarkGenerationItemStatus.Pending,
            TargetQuestionId = t.Id,
            TargetQuestionOrderIndex = t.OrderIndex,
            TargetQuestionExcerpt = t.Text.Length <= 120 ? t.Text : t.Text.Substring(0, 120)
        }).ToList();

        return new BenchmarkGenerationJob
        {
            SuiteId = suiteId,
            SuiteName = suiteName,
            GeneratorConfigId = generator.ConfigId,
            GeneratorDisplayName = generator.DisplayName,
            GeneratorProvider = generator.Provider,
            GeneratorModelId = generator.ModelId,
            GeneratorThinkingLevel = generator.ThinkingLevel,
            GeneratorReasoningMode = generator.ReasoningMode,
            GeneratorServiceTier = generator.ServiceTier,
            GameSnapshotId = snapshotId,
            GameSnapshotName = snapshotName,
            Instructions = instructions,
            StartedByUserId = startedByUserId,
            JobKind = "Regeneration",
            Items = items
        };
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
                GeneratorProvider = GeneratorProvider,
                GeneratorModelId = GeneratorModelId,
                GeneratorThinkingLevel = GeneratorThinkingLevel,
                GeneratorReasoningMode = GeneratorReasoningMode,
                GeneratorServiceTier = GeneratorServiceTier,
                GameSnapshotId = GameSnapshotId,
                GameSnapshotName = GameSnapshotName,
                Instructions = Instructions,
                JobKind = JobKind,
                RetryOfJobId = RetryOfJobId,
                StartedByUserId = StartedByUserId,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Status = Status.ToString(),
                TotalModelCalls = TotalModelCalls,
                PromptTokens = PromptTokens,
                OutputTokens = OutputTokens,
                Items = Items.Select(i => new QuestionGenerationJobItemDto
                {
                    Kind = i.Kind.ToString(),
                    Difficulty = (int)i.Difficulty,
                    DifficultyName = i.Difficulty.ToString(),
                    RequestedCount = i.RequestedCount,
                    GeneratedCount = i.GeneratedCount,
                    Status = i.Status.ToString(),
                    ErrorMessage = i.ErrorMessage,
                    TargetQuestionId = i.TargetQuestionId,
                    TargetQuestionOrderIndex = i.TargetQuestionOrderIndex,
                    TargetQuestionExcerpt = i.TargetQuestionExcerpt,
                    StartedAtUtc = i.StartedAtUtc,
                    CompletedAtUtc = i.CompletedAtUtc,
                    ModelCalls = i.ModelCalls,
                    PromptTokens = i.PromptTokens,
                    OutputTokens = i.OutputTokens,
                    CreatedQuestionCount = i.CreatedQuestionIds.Count,
                    UpdatedQuestionCount = i.UpdatedQuestionIds.Count,
                    DiscardedQuestionCount = i.QuestionIdsToDiscard.Count
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
