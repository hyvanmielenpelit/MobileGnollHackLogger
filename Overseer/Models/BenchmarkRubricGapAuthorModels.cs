namespace Overseer.Models;

using System;
using System.Collections.Generic;

// DTOs for the Rubric Gap Author — the job that *drafts* rubric additions for a human to accept.
// They live in their own file rather than in BenchmarkAdminModels.cs because the boundary this
// feature enforces (a model drafts, a human accepts, one draft at a time) is easier to keep intact
// when its whole contract is readable in one place.

public class StartRubricGapAuthorRequest
{
    public long SuiteId { get; set; }

    /// <summary>Optional cluster subset. Null or empty means every eligible cluster in the suite.</summary>
    public List<string>? ClusterKeys { get; set; }

    public long AuthorModelConfigurationId { get; set; }

    /// <summary>Free-text operator instructions, passed through to the prompt verbatim.</summary>
    public string? Instructions { get; set; }
}

public class RubricGapAuthorDraftDto
{
    /// <summary>Stable identifier for the cluster this draft answers: `questionId:index`.</summary>
    public string ClusterKey { get; set; } = string.Empty;

    public long QuestionId { get; set; }
    public int QuestionOrderIndex { get; set; }
    public string QuestionTextExcerpt { get; set; } = string.Empty;

    /// <summary>The cluster's claims verbatim, so the operator reads what was actually said.</summary>
    public List<string> Claims { get; set; } = new();

    /// <summary>VerifiedRubricGap or LikelyRubricGap. LikelyHallucination clusters are never eligible.</summary>
    public string ClusterVerdict { get; set; } = string.Empty;

    public int Occurrences { get; set; }
    public List<string> ModelFamilies { get; set; } = new();

    public string Status { get; set; } = string.Empty;

    /// <summary>The proposed rubric addition. This is a draft; it is not a rubric.</summary>
    public string? ProposedText { get; set; }

    /// <summary>Required for every proposed addition. A draft without one is rejected.</summary>
    public string? Citation { get; set; }

    public string? Justification { get; set; }

    /// <summary>The model's own statement of how confident it is, in its own words.</summary>
    public string? ConfidenceNote { get; set; }

    public string? ErrorMessage { get; set; }
}

public class RubricGapAuthorJobLogEntryDto
{
    public DateTime TimestampUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

public class RubricGapAuthorJobDto
{
    public string Id { get; set; } = string.Empty;
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public long AuthorConfigId { get; set; }
    public string AuthorDisplayName { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public string? StartedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public List<RubricGapAuthorDraftDto> Drafts { get; set; } = new();
    public List<RubricGapAuthorJobLogEntryDto> Log { get; set; } = new();
}

/// <summary>
/// One acceptance of one draft. There is no list form of this request on purpose: an accept-all
/// button would defeat the human-authorship boundary the whole feature exists to keep.
/// </summary>
public class AcceptRubricAdditionRequest
{
    /// <summary>
    /// The text to append to the rubric — whatever the operator submits, which may be the draft,
    /// an edit of it, or a replacement. The endpoint stores this, not the draft.
    /// </summary>
    public string AcceptedText { get; set; } = string.Empty;

    /// <summary>The job the draft came from, so the drafting model can be recorded.</summary>
    public string? JobId { get; set; }

    /// <summary>The draft's cluster key within that job.</summary>
    public string? ClusterKey { get; set; }
}

public class RubricAdditionAcceptanceDto
{
    public long Id { get; set; }
    public long QuestionId { get; set; }
    public int QuestionOrderIndex { get; set; }
    public int ItemRevisionAfter { get; set; }
    public bool AcceptedVerbatim { get; set; }
    public string? Citation { get; set; }
    public string? AuthorModelDisplayName { get; set; }
    public DateTime AcceptedAtUtc { get; set; }

    /// <summary>The question's full rubric after the addition, so the caller need not re-fetch.</summary>
    public string? ExpectedPoints { get; set; }
}
