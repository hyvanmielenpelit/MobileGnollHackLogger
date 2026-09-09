namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// One tool call emitted during a benchmark answer's turn, recorded in emission order.
///
/// Deliberately not <see cref="ChatMessageToolCall"/>: that record's foreign key is to a
/// <see cref="ChatMessage"/>, which a benchmark answer has none of, and the retention sweep that
/// prunes that table joins through <c>ChatMessage.TimestampUtc</c> — a benchmark row would be
/// silently invisible to it.
///
/// <see cref="ArgsText"/>, <see cref="Result"/> and <see cref="Error"/> are payloads and are
/// prunable: the retention sweep nulls <see cref="ArgsText"/> and <see cref="Result"/> after a
/// configured age. Every other field is a few bytes and is never pruned, because it is what the
/// aggregates and diagnostics read.
///
/// Rows exist only for runs from harness version 17 onward. An answer with no rows means the run
/// predates this record, never that the model made no tool calls —
/// <see cref="BenchmarkRunAnswer.ToolCallSummary"/> remains the only source for those.
/// </summary>
public class BenchmarkRunAnswerToolCall
{
    public long Id { get; set; }

    public long BenchmarkRunAnswerId { get; set; }
    public BenchmarkRunAnswer? BenchmarkRunAnswer { get; set; }

    /// <summary>Emission order across the whole turn, 0-based and dense.</summary>
    public int SortOrder { get; set; }

    /// <summary>The tool round this call belongs to, from <c>ChatMessageToolCall.BatchIndex</c>.</summary>
    public int? IterationIndex { get; set; }

    [MaxLength(256)]
    public string? Name { get; set; }

    [MaxLength(128)]
    public string? ToolCallId { get; set; }

    [MaxLength(32)]
    public string? Status { get; set; }

    /// <summary>Payload; nulled by the retention sweep after a configured age.</summary>
    public string? ArgsText { get; set; }

    /// <summary>Payload; nulled by the retention sweep after a configured age.</summary>
    public string? Result { get; set; }

    /// <summary>Payload; unbounded, capped in application code.</summary>
    public string? Error { get; set; }

    public int? QueueWaitMs { get; set; }

    public int? ExecutionMs { get; set; }

    public int Depth { get; set; }

    [MaxLength(128)]
    public string? AgentName { get; set; }

    public bool ArgsTruncated { get; set; }

    public bool ResultTruncated { get; set; }

    /// <summary>
    /// The true result length in characters before capping — recorded even when nothing was
    /// truncated, because the true size is itself a diagnostic signal, and it survives the payload
    /// prune above. Zero when the call produced no result. This is what keeps a truncated or
    /// pruned record usable: it distinguishes "the tool returned nothing" from "the tool returned
    /// 40 KB and the first 12 were stored".
    /// </summary>
    public int ResultLengthChars { get; set; }
}
