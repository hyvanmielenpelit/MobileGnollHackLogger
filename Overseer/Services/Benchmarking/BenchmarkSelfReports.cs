namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Feedback;
using Overseer.Services.Tools;

/// <summary>
/// The candidate's <c>report_feedback</c> calls in a benchmark answer's turn, stored as
/// <see cref="BenchmarkRunAnswerSelfReport"/> rows apart from the tool-call record.
///
/// <para><see cref="BenchmarkToolCallRecorder.Build"/> skips every call <see cref="IsSelfReport"/>
/// accepts, so no tool metric counts a self-report; <see cref="Extract"/> builds the rows those calls
/// become instead, from the same list.</para>
/// </summary>
public static class BenchmarkSelfReports
{
    private const string CompletedStatus = "completed";

    private const int ErrorColumnLength = 500;
    private const int FingerprintColumnLength = 64;
    private const int CategoryColumnLength = 32;
    private const int RelatedToolsJsonColumnLength = 1000;
    private const int NoticedViaColumnLength = 32;
    private const int SuspectedCauseColumnLength = 32;

    /// <summary>Whether <paramref name="toolName"/> names the feedback tool, case-insensitively.</summary>
    public static bool IsSelfReport(string? toolName)
        => string.Equals(toolName, ReportFeedbackTool.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the self-report rows for one answer's turn from the run's tool calls.
    ///
    /// <para>Calls are taken in enumeration order, which is emission order, and
    /// <see cref="BenchmarkRunAnswerSelfReport.SortOrder"/> is dense from 0 over the self-report calls
    /// alone. A call is <see cref="BenchmarkRunAnswerSelfReport.Accepted"/> when its status is
    /// <c>completed</c>; otherwise its error is kept, capped at 500 characters. Arguments that do not
    /// parse leave the fingerprint, the category and every text field null, and the parse error stands
    /// in for a missing call error. <paramref name="isKnownTool"/> filters <c>related_tools</c> as the
    /// handler does.</para>
    ///
    /// <para>No foreign key is set, because the answer may not have one yet.</para>
    /// </summary>
    public static List<BenchmarkRunAnswerSelfReport> Extract(
        IEnumerable<ChatMessageToolCall> calls,
        Func<string, bool> isKnownTool)
    {
        var rows = new List<BenchmarkRunAnswerSelfReport>();
        if (calls == null) return rows;

        isKnownTool ??= _ => false;

        int sortOrder = 0;
        foreach (var call in calls)
        {
            if (call == null || !IsSelfReport(call.Name)) continue;

            bool accepted = string.Equals(call.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase);
            bool parsed = ReportFeedbackArguments.TryParse(call.ArgsText, isKnownTool, out var args, out string? parseError);

            string? error = accepted ? null : call.Error;
            if (!parsed && string.IsNullOrEmpty(error))
            {
                error = parseError;
            }

            var row = new BenchmarkRunAnswerSelfReport
            {
                SortOrder = sortOrder++,
                IterationIndex = call.BatchIndex,
                Accepted = accepted,
                Error = Clip(error, ErrorColumnLength)
            };

            if (parsed && args is not null)
            {
                row.Fingerprint = Clip(FeedbackFingerprint.Compute(args), FingerprintColumnLength);
                row.Category = Clip(args.Category, CategoryColumnLength);
                row.Subject = Clip(args.Subject, ReportFeedbackArguments.SubjectMax);
                row.Summary = Clip(args.Summary, ReportFeedbackArguments.SummaryMax);
                row.Details = Clip(args.Details, ReportFeedbackArguments.DetailsMax);
                row.SearchedFor = Clip(args.SearchedFor, ReportFeedbackArguments.SearchedForMax);
                row.ExpectedLocation = Clip(args.ExpectedLocation, ReportFeedbackArguments.ExpectedLocationMax);
                row.RelatedToolsJson = RelatedToolsJson(args.RelatedTools);
                row.NoticedVia = Clip(args.NoticedVia, NoticedViaColumnLength);
                row.SuspectedCause = Clip(args.SuspectedCause, SuspectedCauseColumnLength);
                row.SuspectedCauseNote = Clip(args.SuspectedCauseNote, ReportFeedbackArguments.SuspectedCauseNoteMax);
                row.Truncated = args.Truncated;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// The validated tool names as a JSON array, or null when there are none. Names are dropped from
    /// the end until the array fits its column.
    /// </summary>
    private static string? RelatedToolsJson(List<string>? tools)
    {
        if (tools == null || tools.Count == 0) return null;

        var kept = new List<string>(tools);
        while (kept.Count > 0)
        {
            string json = JsonSerializer.Serialize(kept);
            if (json.Length <= RelatedToolsJsonColumnLength) return json;
            kept.RemoveAt(kept.Count - 1);
        }

        return null;
    }

    /// <summary>Hard-clips a field to its column length, so an over-long value cannot fail the answer's save.</summary>
    private static string? Clip(string? value, int columnLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= columnLength) return value;
        return value.Substring(0, columnLength);
    }
}
