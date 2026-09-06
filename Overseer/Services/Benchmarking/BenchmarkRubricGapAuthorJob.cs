namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Overseer.Models;

public enum BenchmarkRubricGapAuthorJobStatus { Running, Completed, CompletedWithErrors, Cancelled, Failed }
public enum BenchmarkRubricGapAuthorDraftStatus { Pending, Drafting, Completed, Failed, Skipped }

/// <summary>
/// One drafted rubric addition, held in memory for the length of the job.
///
/// It is a *draft*, never a rubric. Nothing in this type is written to a question; the only path
/// from here into a rubric is an explicit human acceptance of one draft at a time, which carries
/// the operator's own text rather than <see cref="ProposedText"/>.
/// </summary>
public class BenchmarkRubricGapAuthorDraft
{
    /// <summary>`questionId:index` — stable for the life of the job, which is all an accept needs.</summary>
    public string ClusterKey { get; set; } = string.Empty;

    public long QuestionId { get; set; }
    public int QuestionOrderIndex { get; set; }
    public string QuestionTextExcerpt { get; set; } = string.Empty;

    public List<string> Claims { get; set; } = new();
    public List<string> ModelFamilies { get; set; } = new();
    public int Occurrences { get; set; }
    public BenchmarkRubricGapVerdict ClusterVerdict { get; set; }

    public BenchmarkRubricGapAuthorDraftStatus Status { get; set; } = BenchmarkRubricGapAuthorDraftStatus.Pending;

    public string? ProposedText { get; set; }
    public string? Citation { get; set; }
    public string? Justification { get; set; }
    public string? ConfidenceNote { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BenchmarkRubricGapAuthorJobLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

/// <summary>
/// In-memory state for one Rubric Gap Author run, modelled on
/// <see cref="BenchmarkRubricCheckJob"/> so an operator who knows one panel knows the other.
/// </summary>
public class BenchmarkRubricGapAuthorJob
{
    private readonly object _lock = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public long AuthorConfigId { get; set; }
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>The provider and model id actually used, recorded on every acceptance.</summary>
    public string AuthorProviderUsed { get; set; } = string.Empty;
    public string AuthorModelIdUsed { get; set; } = string.Empty;

    /// <summary>The operator's free-text instructions, kept so the job's output is explainable.</summary>
    public string? Instructions { get; set; }

    public string? StartedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public BenchmarkRubricGapAuthorJobStatus Status { get; set; } = BenchmarkRubricGapAuthorJobStatus.Running;

    public List<BenchmarkRubricGapAuthorDraft> Drafts { get; set; } = new();
    public List<BenchmarkRubricGapAuthorJobLogEntry> Log { get; set; } = new();

    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }

    public CancellationTokenSource Cts { get; set; } = null!;

    public void AddLog(string message, string severity = "info", string? rawExcerpt = null)
    {
        lock (_lock)
        {
            Log.Add(new BenchmarkRubricGapAuthorJobLogEntry
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

    public void SetDraftResult(string clusterKey, string proposedText, string citation, string? justification, string? confidenceNote)
    {
        lock (_lock)
        {
            var draft = Drafts.FirstOrDefault(d => d.ClusterKey == clusterKey);
            if (draft != null)
            {
                draft.Status = BenchmarkRubricGapAuthorDraftStatus.Completed;
                draft.ProposedText = proposedText;
                draft.Citation = citation;
                draft.Justification = justification;
                draft.ConfidenceNote = confidenceNote;
                draft.ErrorMessage = null;
            }
        }
    }

    public void SetDraftFailed(string clusterKey, string errorMessage)
    {
        lock (_lock)
        {
            var draft = Drafts.FirstOrDefault(d => d.ClusterKey == clusterKey);
            if (draft != null)
            {
                draft.Status = BenchmarkRubricGapAuthorDraftStatus.Failed;
                draft.ErrorMessage = errorMessage;
            }
        }
    }

    public BenchmarkRubricGapAuthorDraft? TryGetDraft(string clusterKey)
    {
        lock (_lock)
        {
            return Drafts.FirstOrDefault(d => d.ClusterKey == clusterKey);
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

    public void SetStatus(BenchmarkRubricGapAuthorJobStatus status)
    {
        lock (_lock)
        {
            Status = status;
            if (status != BenchmarkRubricGapAuthorJobStatus.Running && CompletedAtUtc == null)
            {
                CompletedAtUtc = DateTime.UtcNow;
            }
        }
    }

    public RubricGapAuthorJobDto ToDto()
    {
        lock (_lock)
        {
            return new RubricGapAuthorJobDto
            {
                Id = Id,
                SuiteId = SuiteId,
                SuiteName = SuiteName,
                AuthorConfigId = AuthorConfigId,
                AuthorDisplayName = AuthorDisplayName,
                Instructions = Instructions,
                StartedByUserId = StartedByUserId,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Status = Status.ToString(),
                TotalModelCalls = TotalModelCalls,
                PromptTokens = PromptTokens,
                OutputTokens = OutputTokens,
                Drafts = Drafts.Select(d => new RubricGapAuthorDraftDto
                {
                    ClusterKey = d.ClusterKey,
                    QuestionId = d.QuestionId,
                    QuestionOrderIndex = d.QuestionOrderIndex,
                    QuestionTextExcerpt = d.QuestionTextExcerpt,
                    Claims = d.Claims.ToList(),
                    ClusterVerdict = d.ClusterVerdict.ToString(),
                    Occurrences = d.Occurrences,
                    ModelFamilies = d.ModelFamilies.ToList(),
                    Status = d.Status.ToString(),
                    ProposedText = d.ProposedText,
                    Citation = d.Citation,
                    Justification = d.Justification,
                    ConfidenceNote = d.ConfidenceNote,
                    ErrorMessage = d.ErrorMessage
                }).ToList(),
                Log = Log.Select(l => new RubricGapAuthorJobLogEntryDto
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
