namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

/// <summary>
/// The three payload caps a recorded benchmark tool call is stored under, in characters.
///
/// A cap of zero or less means "no cap" — the payload is stored whole. That is a deliberate
/// escape hatch for an operator who wants the raw record, not an error state.
/// </summary>
/// <param name="MaxArgsChars">
/// Cap on <see cref="BenchmarkRunAnswerToolCall.ArgsText"/>. Arguments are the model's own text,
/// so their size is bounded by what a model will emit rather than by anything the harness sets.
/// </param>
/// <param name="MaxResultChars">
/// Cap on <see cref="BenchmarkRunAnswerToolCall.Result"/>. Derived from the run's own
/// <c>Benchmark:MaxResultLength</c> rather than written as a literal — see
/// <see cref="Resolve"/> for why the derivation is the point.
/// </param>
/// <param name="MaxErrorChars">
/// Cap on <see cref="BenchmarkRunAnswerToolCall.Error"/>. There is no truncation flag for the
/// error: a tool error's diagnostic content is in its first line, and a cut one is still that.
/// </param>
public sealed record BenchmarkToolCallRecordLimits(
    int MaxArgsChars,
    int MaxResultChars,
    int MaxErrorChars)
{
    /// <summary>Effective default for <see cref="MaxArgsChars"/>.</summary>
    public const int DefaultMaxArgsChars = 4000;

    /// <summary>Effective default for <see cref="MaxErrorChars"/>.</summary>
    public const int DefaultMaxErrorChars = 2000;

    /// <summary>
    /// Headroom added to the agent context's result length when deriving the default
    /// <see cref="MaxResultChars"/>, so that a result sitting exactly at the agent's own cap is
    /// stored whole instead of losing its tail to a second truncation. Matches the cap-plus-headroom
    /// pattern <c>take_snapshot</c> already uses (60,200 for a 60,000 cap).
    /// </summary>
    public const int ResultHeadroomChars = 2000;

    /// <summary>
    /// Resolves the caps from configuration, falling back to the effective defaults.
    ///
    /// <paramref name="maxResultLength"/> is the already-resolved <c>Benchmark:MaxResultLength</c>
    /// the caller passes to the agent context, and the default for <see cref="MaxResultChars"/> is
    /// derived from it — <c>maxResultLength + <see cref="ResultHeadroomChars"/></c> — never written
    /// as a literal. <c>ToolExecutor</c> truncates every successful tool result to that same figure
    /// *before* the content reaches a record, so a stored result cannot exceed it at the current
    /// configuration: a hardcoded 16,000 would be a cap that never fires, expressed as a magic
    /// number that silently becomes wrong the day an operator raises <c>Benchmark:MaxResultLength</c>.
    ///
    /// The derivation also covers a ceiling the benchmark does not set. <c>Benchmark:AllowedTools</c>
    /// is operator-configurable; adding a sub-agent tool would put that tool's results under
    /// <c>MaxSubAgentResultLength</c> (30,000) instead, which the benchmark never configures. Reading
    /// the cap from the same value the agent context was given is what keeps the record honest if
    /// either figure moves.
    ///
    /// Configuration keys: <c>Benchmark:ToolCallRecord:MaxArgsChars</c>,
    /// <c>Benchmark:ToolCallRecord:MaxResultChars</c>, <c>Benchmark:ToolCallRecord:MaxErrorChars</c>.
    /// </summary>
    public static BenchmarkToolCallRecordLimits Resolve(IConfiguration configuration, int maxResultLength)
    {
        int derivedResultCap = maxResultLength > 0 ? maxResultLength + ResultHeadroomChars : 0;

        if (configuration == null)
        {
            return new BenchmarkToolCallRecordLimits(DefaultMaxArgsChars, derivedResultCap, DefaultMaxErrorChars);
        }

        return new BenchmarkToolCallRecordLimits(
            MaxArgsChars: configuration.GetValue<int>("Benchmark:ToolCallRecord:MaxArgsChars", DefaultMaxArgsChars),
            MaxResultChars: configuration.GetValue<int>("Benchmark:ToolCallRecord:MaxResultChars", derivedResultCap),
            MaxErrorChars: configuration.GetValue<int>("Benchmark:ToolCallRecord:MaxErrorChars", DefaultMaxErrorChars));
    }
}

/// <summary>
/// Pure static translator from an agent run's live <see cref="ChatMessageToolCall"/> list to the
/// <see cref="BenchmarkRunAnswerToolCall"/> rows stored against a benchmark answer, plus the
/// outcome counts those rows support.
///
/// What this exists for: <see cref="BenchmarkRunAnswer.ToolCallSummary"/> lists only the calls that
/// succeeded. A call whose JSON result exceeded the result cap is converted by <c>ToolExecutor</c>
/// into a *failed* call carrying "Result too large (N chars)", and a call refused for exhausted
/// budget is failed too — so a model that repeatedly over-fetched from <c>source_code_search</c>
/// leaves no trace at all in the summary. The rows built here are the record that does.
/// </summary>
public static class BenchmarkToolCallRecorder
{
    /// <summary>
    /// The substring <c>ToolExecutor</c> puts in the error of a call it refused because the tool
    /// budget was already spent, when the budget was scoped to the whole session.
    /// </summary>
    public const string BudgetRefusalMarker = "Maximum tool calls per session exceeded";

    /// <summary>
    /// The same refusal, worded for a per-question budget scope. <c>ToolExecutor</c> chooses
    /// between the two on whether <c>ToolExecutionContext.ToolBudgetScopeId</c> is set, and every
    /// benchmark executor sets it (<c>bench_{runId}_q{orderIndex}</c>) — so a benchmark refusal
    /// carries <b>this</b> wording and never <see cref="BudgetRefusalMarker"/>.
    ///
    /// Matching only the session literal is what left the <c>"(N blocked by budget)"</c> suffix of
    /// <see cref="BenchmarkRunAnswer.ToolCallSummary"/> reading zero on every benchmark run since
    /// per-question scoping was introduced, and it would have put every refused call in
    /// <see cref="Outcomes"/>' failed bucket — the one distinction the split exists to draw.
    /// </summary>
    public const string PerQuestionBudgetRefusalMarker = "Tool call budget for this question is exhausted";

    private const string ArgsTruncationMarker = "... [Arguments truncated for length]";
    private const string ResultTruncationMarker = "... [Result truncated for length]";
    private const string ErrorTruncationMarker = "... [Error truncated for length]";

    private const int NameColumnLength = 256;
    private const int ToolCallIdColumnLength = 128;
    private const int StatusColumnLength = 32;
    private const int AgentNameColumnLength = 128;

    private const string CompletedStatus = "completed";

    /// <summary>
    /// True when <paramref name="error"/> is either refusal <c>ToolExecutor</c> emits once the tool
    /// budget is spent. A refused call ran no tool code at all, which is why it is counted apart
    /// from a call that ran and failed.
    ///
    /// Both wordings are matched because the refusal message depends on the budget's scope, and a
    /// benchmark run is always per-question scoped. This is the single owner of that test: the
    /// <c>ToolCallSummary</c> blocked-count and <see cref="Outcomes"/> both call it, so the two
    /// cannot disagree about what "refused" means.
    /// </summary>
    public static bool IsBudgetRefusal(string? error)
    {
        if (string.IsNullOrEmpty(error)) return false;

        return error.Contains(BudgetRefusalMarker, StringComparison.Ordinal)
            || error.Contains(PerQuestionBudgetRefusalMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the stored rows for one answer's turn.
    ///
    /// <paramref name="calls"/> is consumed in enumeration order, and that order *is* emission
    /// order: <see cref="BenchmarkRunAnswerToolCall.SortOrder"/> is assigned densely from 0 over
    /// what is enumerated. The source <see cref="ChatMessageToolCall.SortOrder"/> is deliberately
    /// not sorted on — nested sub-agent calls are appended to the list after their parent completes
    /// and carry no meaningful value in that field, so sorting by it would scramble the turn.
    ///
    /// The returned list is a plain <see cref="List{T}"/> ready to assign to
    /// <see cref="BenchmarkRunAnswer.ToolCalls"/>; no foreign key is set, because the answer may not
    /// have one yet.
    /// </summary>
    public static List<BenchmarkRunAnswerToolCall> Build(
        IEnumerable<ChatMessageToolCall> calls,
        BenchmarkToolCallRecordLimits limits)
    {
        var rows = new List<BenchmarkRunAnswerToolCall>();
        if (calls == null) return rows;

        int maxArgs = limits?.MaxArgsChars ?? 0;
        int maxResult = limits?.MaxResultChars ?? 0;
        int maxError = limits?.MaxErrorChars ?? 0;

        int sortOrder = 0;
        foreach (var call in calls)
        {
            if (call == null) continue;

            string? args = Cap(call.ArgsText, maxArgs, ArgsTruncationMarker, out bool argsTruncated);
            string? result = Cap(call.Result, maxResult, ResultTruncationMarker, out bool resultTruncated);
            string? error = Cap(call.Error, maxError, ErrorTruncationMarker, out _);

            rows.Add(new BenchmarkRunAnswerToolCall
            {
                SortOrder = sortOrder++,
                IterationIndex = call.BatchIndex,
                Name = Clip(call.Name, NameColumnLength),
                ToolCallId = Clip(call.ToolCallId, ToolCallIdColumnLength),
                Status = Clip(call.Status, StatusColumnLength),
                ArgsText = args,
                Result = result,
                Error = error,
                QueueWaitMs = call.QueueWaitMs,
                ExecutionMs = call.ExecutionMs,
                Depth = call.Depth,
                AgentName = Clip(call.AgentName, AgentNameColumnLength),
                ArgsTruncated = argsTruncated,
                ResultTruncated = resultTruncated,

                // The length of what the tool produced, not of what was stored.
                ResultLengthChars = call.Result?.Length ?? 0
            });
        }

        return rows;
    }

    /// <summary>
    /// Partitions stored rows into the three outcomes a tool-layer diagnosis turns on. The three
    /// buckets are exhaustive and mutually exclusive: they sum to the row count exactly.
    ///
    /// A row is <c>RefusedByBudget</c> when its error is the budget refusal — no tool ran.
    /// Otherwise it is <c>Failed</c> whenever its status is not <c>completed</c>, whatever the
    /// reason: an over-large result converted to an error, a handler exception, a timeout, a
    /// cancellation, or a status the provider never wrote (null and empty both count as not
    /// completed). Everything else is <c>Succeeded</c>. The comparison is case-insensitive.
    ///
    /// This is the count a reader of <see cref="BenchmarkRunAnswer.ToolCallSummary"/> alone has to
    /// derive by subtracting the summary's successes from
    /// <see cref="BenchmarkRunAnswer.ToolCallCount"/>, which conflates a refusal with a failure.
    /// </summary>
    public static (int Succeeded, int Failed, int RefusedByBudget) Outcomes(
        IEnumerable<BenchmarkRunAnswerToolCall> rows)
    {
        int succeeded = 0;
        int failed = 0;
        int refused = 0;

        if (rows == null) return (0, 0, 0);

        foreach (var row in rows)
        {
            if (row == null) continue;

            if (IsBudgetRefusal(row.Error))
            {
                refused++;
            }
            else if (!string.Equals(row.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase))
            {
                failed++;
            }
            else
            {
                succeeded++;
            }
        }

        return (succeeded, failed, refused);
    }

    /// <summary>
    /// Caps a payload at <paramref name="limit"/> characters, appending <paramref name="marker"/>
    /// within the cap so the stored text is self-describing and never exceeds the limit.
    /// A limit of zero or less caps nothing.
    /// </summary>
    private static string? Cap(string? value, int limit, string marker, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrEmpty(value) || limit <= 0 || value.Length <= limit) return value;

        truncated = true;
        if (limit <= marker.Length) return value.Substring(0, limit);

        return value.Substring(0, limit - marker.Length) + marker;
    }

    /// <summary>
    /// Hard-clips a short field to its database column length. Defensive only: a provider that
    /// returns an over-long tool name or call id would otherwise throw at save time and lose the
    /// whole answer, not just the field.
    /// </summary>
    private static string? Clip(string? value, int columnLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= columnLength) return value;
        return value.Substring(0, columnLength);
    }
}
