namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// One <c>report_feedback</c> call a benchmark candidate made during an answer's turn, in emission
/// order. Recorded from harness version 60.
///
/// <para>Kept apart from <see cref="BenchmarkRunAnswerToolCall"/> so no tool metric counts it. The
/// text is stored as the candidate wrote it, unscrubbed, and lives as long as its answer.</para>
/// </summary>
public class BenchmarkRunAnswerSelfReport
{
    public long Id { get; set; }

    public long BenchmarkRunAnswerId { get; set; }
    public BenchmarkRunAnswer? BenchmarkRunAnswer { get; set; }

    /// <summary>Emission order across the answer's self-report calls, 0-based and dense.</summary>
    public int SortOrder { get; set; }

    /// <summary>The tool round this call belongs to, from <c>ChatMessageToolCall.BatchIndex</c>.</summary>
    public int? IterationIndex { get; set; }

    /// <summary>The call completed; false when it was refused or its arguments were invalid.</summary>
    public bool Accepted { get; set; }

    /// <summary>The refusal or validation message when not accepted.</summary>
    [MaxLength(500)]
    public string? Error { get; set; }

    /// <summary>Null when the arguments did not parse.</summary>
    [MaxLength(64)]
    public string? Fingerprint { get; set; }

    /// <summary>Null when the arguments did not parse.</summary>
    [MaxLength(32)]
    public string? Category { get; set; }

    [MaxLength(120)]
    public string? Subject { get; set; }

    [MaxLength(300)]
    public string? Summary { get; set; }

    [MaxLength(2000)]
    public string? Details { get; set; }

    [MaxLength(500)]
    public string? SearchedFor { get; set; }

    [MaxLength(300)]
    public string? ExpectedLocation { get; set; }

    /// <summary>JSON array of validated tool names.</summary>
    [MaxLength(1000)]
    public string? RelatedToolsJson { get; set; }

    [MaxLength(32)]
    public string? NoticedVia { get; set; }

    /// <summary>The candidate's hypothesis, not a finding.</summary>
    [MaxLength(32)]
    public string? SuspectedCause { get; set; }

    [MaxLength(500)]
    public string? SuspectedCauseNote { get; set; }

    public bool Truncated { get; set; }
}
