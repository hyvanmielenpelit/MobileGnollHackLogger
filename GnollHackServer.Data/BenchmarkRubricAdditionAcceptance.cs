namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// The provenance record for one rubric addition a human accepted from a Rubric Gap Author draft.
///
/// It exists because the authorship claim needs a *stored fact*, not an assumption. § 7 rung 1 of
/// the benchmark-to-chat-transfer method requires human authorship of curated knowledge, and
/// <see cref="Overseer.Services.Benchmarking.BenchmarkRubricGapDetector"/> documents that a gap is
/// surfaced for a human to fold into the rubric and is never applied automatically. This row is
/// what later answers "did a human actually read this?": it keeps the drafting model, the cluster
/// the draft came from, its citation, the text that was actually applied, and whether that text was
/// the model's verbatim or the operator's edit of it.
///
/// One row per acceptance. There is deliberately no bulk-accept path, so there is deliberately no
/// batched form of this record either.
/// </summary>
public class BenchmarkRubricAdditionAcceptance
{
    public long Id { get; set; }

    public long BenchmarkQuestionId { get; set; }
    public BenchmarkQuestion BenchmarkQuestion { get; set; } = default!;

    /// <summary>The question's ItemRevision *after* the acceptance bumped it.</summary>
    public int ItemRevisionAfter { get; set; }

    /// <summary>The rubric text that was actually appended — the operator's submission, never the draft.</summary>
    public string AcceptedText { get; set; } = default!;

    /// <summary>The model's draft, kept verbatim so the edit is reconstructable.</summary>
    public string DraftText { get; set; } = default!;

    /// <summary>
    /// True when <see cref="AcceptedText"/> is character-identical to <see cref="DraftText"/>.
    /// The distinction is the point of the record: a verbatim acceptance and an edited one are
    /// different authorship claims and must not be conflated.
    /// </summary>
    public bool AcceptedVerbatim { get; set; }

    /// <summary>The citation the draft was required to carry. Null only for a draft that carried none.</summary>
    [MaxLength(512)]
    public string? Citation { get; set; }

    /// <summary>The first claim of the cluster the draft answered, so the row is readable on its own.</summary>
    public string? ClusterClaim { get; set; }

    public long? AuthorModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? AuthorModelConfiguration { get; set; }

    [MaxLength(64)]
    public string? AuthorProviderUsed { get; set; }

    [MaxLength(128)]
    public string? AuthorModelIdUsed { get; set; }

    [MaxLength(256)]
    public string? AuthorModelDisplayName { get; set; }

    [MaxLength(450)]
    public string? AcceptedByUserId { get; set; }

    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;
}
