namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;

/// <summary>
/// A read-only Markdown export of one benchmark run's complete tool-call record — every
/// argument, result and error a run's tool calls carried, for reading offline. Payloads are
/// rendered inside fenced code blocks rather than table cells: a table cell cannot safely carry
/// arbitrary tool output (pipes, backticks, embedded fences) without corrupting the table, while a
/// fence picked long enough that the payload's own fences cannot close it early can hold it
/// unescaped and byte-identical.
/// </summary>
public static class BenchmarkToolCallLogBuilder
{
    private static string Inv(IFormattable value, string? format = null)
        => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Stamp(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // The head size a long Result is cut to inside its fenced block.
    private const int ResultHeadChars = 600;

    /// <summary>
    /// The sentence for an answer with zero tool-call rows. A run before harness 17 never recorded
    /// per-call rows at all, so "recorded none" is the only honest reading — an unparseable or
    /// missing <see cref="BenchmarkRun.HarnessVersion"/> is treated the same way, since such a run
    /// predates the version string itself. From harness 17 on, an empty row set means the turn made
    /// no calls, and a terminal-failure answer (<see cref="BenchmarkRunFinalizer.HasTerminalFailure"/>)
    /// says so specifically: it never reached a tool round.
    /// </summary>
    private static string NoToolCallsSentence(BenchmarkRun run, BenchmarkRunAnswer answer)
    {
        if (!int.TryParse(run.HarnessVersion, out int version) || version < 17)
        {
            return "*No tool-call rows recorded for this answer — a run before harness 17 records none.*";
        }

        return BenchmarkRunFinalizer.HasTerminalFailure(answer)
            ? "*No tool calls attempted — the answer failed before its first tool round.*"
            : "*No tool calls attempted on this answer.*";
    }

    public static string Build(BenchmarkRun run, IReadOnlyList<BenchmarkRunAnswer> answers)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Tool Call Log — Run {run.Id}");
        sb.AppendLine();
        sb.AppendLine($"- **Suite:** {run.SuiteName}");
        sb.AppendLine($"- **Model:** {run.TestedModelDisplayNameUsed}");
        sb.AppendLine($"- **Harness Version:** {run.HarnessVersion ?? "1 (unversioned legacy)"}");
        sb.AppendLine($"- **Started (UTC):** {Stamp(run.StartedAtUtc)}");
        sb.AppendLine($"- **Candidate System Prompt SHA-256:** {run.CandidateSystemPromptSha256 ?? "not recorded"}");
        sb.AppendLine($"- **ToolGuides SHA-256:** {run.ToolGuidesSha256 ?? "not recorded"}");
        sb.AppendLine($"- **Knowledge Base HEAD SHA:** {run.KnowledgeBaseHeadSha ?? "not recorded"}");
        sb.AppendLine($"- **GnollHack Wiki HEAD SHA:** {run.WikiHeadSha ?? "not recorded"}");
        sb.AppendLine($"- **GnollHack Source HEAD SHA:** {run.SourceCodeHeadSha ?? "not recorded"}");
        sb.AppendLine();

        foreach (var answer in answers.OrderBy(a => a.OrderIndex))
        {
            sb.AppendLine($"## Question {answer.OrderIndex}: {answer.QuestionText}");
            sb.AppendLine();

            if (answer.ToolCalls.Count == 0)
            {
                sb.AppendLine(NoToolCallsSentence(run, answer));
                sb.AppendLine();
                continue;
            }

            var orderedCalls = answer.ToolCalls.OrderBy(c => c.SortOrder).ToList();

            sb.AppendLine("| SortOrder | IterationIndex | Name | Status | QueueWaitMs | ExecutionMs | ResultLengthChars | ArgsTruncated | ResultTruncated |");
            sb.AppendLine("|----------:|---------------:|------|--------|------------:|------------:|-------------------:|:-------------:|:---------------:|");
            foreach (var call in orderedCalls)
            {
                string iteration = call.IterationIndex.HasValue ? Inv(call.IterationIndex.Value) : "N/A";
                string name = string.IsNullOrWhiteSpace(call.Name) ? "*(unnamed)*" : $"`{call.Name}`";
                string status = string.IsNullOrWhiteSpace(call.Status) ? "*(none recorded)*" : call.Status;
                string queueWait = call.QueueWaitMs.HasValue ? Inv(call.QueueWaitMs.Value, "N0") : "N/A";
                string execMs = call.ExecutionMs.HasValue ? Inv(call.ExecutionMs.Value, "N0") : "N/A";
                string resultLen = Inv(call.ResultLengthChars, "N0");
                string argsTrunc = call.ArgsTruncated ? "yes" : "no";
                string resultTrunc = call.ResultTruncated ? "yes" : "no";
                sb.AppendLine($"| {call.SortOrder} | {iteration} | {name} | {status} | {queueWait} | {execMs} | {resultLen} | {argsTrunc} | {resultTrunc} |");
            }
            sb.AppendLine();

            foreach (var call in orderedCalls)
            {
                string label = string.IsNullOrWhiteSpace(call.Name) ? "(unnamed)" : call.Name;
                sb.AppendLine($"**Call {call.SortOrder}** (`{label}`):");
                sb.AppendLine();
                sb.AppendLine(RenderCallBody(call));
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// One call's full <c>ArgsText</c>, its <c>Error</c> if any, and the first
    /// <see cref="ResultHeadChars"/> characters of <c>Result</c>, inside a single fenced block
    /// whose fence is picked longer than any run of backticks the payload itself contains. A
    /// field that is null beside a non-zero <c>ResultLengthChars</c> was nulled by the retention
    /// sweep rather than never recorded, and is marked as such.
    /// </summary>
    private static string RenderCallBody(BenchmarkRunAnswerToolCall call)
    {
        string argsSection = call.ArgsText != null
            ? call.ArgsText
            : (call.ResultLengthChars != 0 ? "(pruned by retention)" : "(none recorded)");

        string errorSection = string.IsNullOrEmpty(call.Error) ? "(none)" : call.Error;

        string resultLabel;
        string resultSection;
        if (call.Result != null)
        {
            if (call.Result.Length > ResultHeadChars)
            {
                resultLabel = $"Result (first {ResultHeadChars} of {Inv(call.Result.Length, "N0")} chars):";
                resultSection = call.Result[..ResultHeadChars] + $"\n… [head of {ResultHeadChars} chars]";
            }
            else
            {
                resultLabel = "Result:";
                resultSection = call.Result;
            }
        }
        else
        {
            resultLabel = "Result:";
            resultSection = call.ResultLengthChars != 0 ? "(pruned by retention)" : "(none recorded)";
        }

        string body = $"Args:\n{argsSection}\n\nError:\n{errorSection}\n\n{resultLabel}\n{resultSection}";
        string fence = PickFence(body);
        return $"{fence}\n{body}\n{fence}";
    }

    /// <summary>
    /// A backtick fence one character longer than the longest run of backticks already present in
    /// <paramref name="content"/>, so the fence can never be closed early by the payload it wraps.
    /// Never shorter than the standard three-backtick fence.
    /// </summary>
    private static string PickFence(string content)
    {
        int longestRun = 0;
        int currentRun = 0;
        foreach (char c in content)
        {
            if (c == '`')
            {
                currentRun++;
                if (currentRun > longestRun) longestRun = currentRun;
            }
            else
            {
                currentRun = 0;
            }
        }

        return new string('`', Math.Max(3, longestRun + 1));
    }
}
