namespace Overseer.Services.Feedback;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;

/// <summary>The <c>category</c> values of <c>report_feedback</c>, as stored and sent over the wire.</summary>
public static class FeedbackCategories
{
    public const string MissingInformation = "missing_information";
    public const string ToolProblem = "tool_problem";
    public const string OwnMistake = "own_mistake";

    public static IReadOnlyList<string> All { get; } = new[] { MissingInformation, ToolProblem, OwnMistake };

    /// <summary>Whether <paramref name="value"/> is one of <see cref="All"/>, compared exactly.</summary>
    public static bool IsKnown(string? value) => value != null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// The validated arguments of one <c>report_feedback</c> call, shared by the chat handler and the
/// benchmark's self-report extraction.
/// </summary>
public sealed class ReportFeedbackArguments
{
    public const int SubjectMax = 120;
    public const int SummaryMax = 300;
    public const int DetailsMax = 2000;
    public const int SearchedForMax = 500;
    public const int ExpectedLocationMax = 300;
    public const int SuspectedCauseNoteMax = 500;
    public const int RelatedToolsMax = 8;

    /// <summary>The <c>noticed_via</c> values.</summary>
    public static IReadOnlyList<string> NoticedViaValues { get; } = new[] { "user_correction", "tool_result", "self_review" };

    /// <summary>The <c>suspected_cause</c> values; an unknown value becomes <see cref="OtherCause"/>.</summary>
    public static IReadOnlyList<string> SuspectedCauseValues { get; } = new[]
    {
        "did_not_verify", "misread_tool_result", "training_knowledge_conflict", "search_strategy",
        "corpus_or_tool_limit", "ambiguous_question", "answered_too_quickly", OtherCause
    };

    public const string OtherCause = "other";

    public string Category { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Details { get; set; }
    public string? SearchedFor { get; set; }
    public string? ExpectedLocation { get; set; }
    public List<string> RelatedTools { get; set; } = new();
    public string? NoticedVia { get; set; }
    public string? SuspectedCause { get; set; }
    public string? SuspectedCauseNote { get; set; }

    /// <summary>A text field was cut to its maximum length.</summary>
    public bool Truncated { get; set; }

    /// <summary>The scrub replaced something; set by <see cref="FeedbackRedaction.Apply"/>.</summary>
    public bool Redacted { get; set; }

    /// <summary>Parses and validates the call's raw JSON arguments.</summary>
    /// <param name="isKnownTool">Whether a name in <c>related_tools</c> is a registered tool.</param>
    public static bool TryParse(string? json, Func<string, bool> isKnownTool,
        [NotNullWhen(true)] out ReportFeedbackArguments? args, out string? error)
    {
        args = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The arguments are missing. 'category', 'subject' and 'summary' are required.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = "The arguments are not valid JSON.";
            return false;
        }

        using (document)
        {
            return TryParse(document.RootElement, isKnownTool, out args, out error);
        }
    }

    /// <summary>Parses and validates the call's arguments.</summary>
    /// <param name="isKnownTool">Whether a name in <c>related_tools</c> is a registered tool.</param>
    public static bool TryParse(JsonElement element, Func<string, bool> isKnownTool,
        [NotNullWhen(true)] out ReportFeedbackArguments? args, out string? error)
    {
        args = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "The arguments must be a JSON object.";
            return false;
        }

        string? category = ReadString(element, "category")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(category))
        {
            error = "The 'category' parameter is required: missing_information, tool_problem or own_mistake.";
            return false;
        }
        if (!FeedbackCategories.IsKnown(category))
        {
            error = "Unknown 'category' value. Use missing_information, tool_problem or own_mistake.";
            return false;
        }

        bool truncated = false;
        string? subject = Cut(ReadString(element, "subject"), SubjectMax, ref truncated);
        if (subject == null)
        {
            error = "The 'subject' parameter is required and must not be blank.";
            return false;
        }

        string? summary = Cut(ReadString(element, "summary"), SummaryMax, ref truncated);
        if (summary == null)
        {
            error = "The 'summary' parameter is required and must not be blank.";
            return false;
        }

        string? noticedVia = null;
        if (category == FeedbackCategories.OwnMistake)
        {
            string? raw = ReadString(element, "noticed_via")?.Trim().ToLowerInvariant();
            noticedVia = raw != null && NoticedViaValues.Contains(raw, StringComparer.Ordinal) ? raw : null;
        }

        string? cause = ReadString(element, "suspected_cause")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(cause))
        {
            cause = null;
        }
        else if (!SuspectedCauseValues.Contains(cause, StringComparer.Ordinal))
        {
            cause = OtherCause;
        }

        args = new ReportFeedbackArguments
        {
            Category = category,
            Subject = subject,
            Summary = summary,
            Details = Cut(ReadString(element, "details"), DetailsMax, ref truncated),
            SearchedFor = Cut(ReadString(element, "searched_for"), SearchedForMax, ref truncated),
            ExpectedLocation = Cut(ReadString(element, "expected_location"), ExpectedLocationMax, ref truncated),
            RelatedTools = ReadRelatedTools(element, isKnownTool),
            NoticedVia = noticedVia,
            SuspectedCause = cause,
            SuspectedCauseNote = Cut(ReadString(element, "suspected_cause_note"), SuspectedCauseNoteMax, ref truncated)
        };
        args.Truncated = truncated;
        error = null;
        return true;
    }

    /// <summary>
    /// <paramref name="value"/> trimmed and cut to <paramref name="max"/> characters, never inside a
    /// surrogate pair; null when blank. Sets <paramref name="truncated"/> when it cuts.
    /// </summary>
    internal static string? Cut(string? value, int max, ref bool truncated)
    {
        if (value == null) return null;
        string trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length <= max) return trimmed;

        truncated = true;
        int length = char.IsHighSurrogate(trimmed[max - 1]) ? max - 1 : max;
        return trimmed.Substring(0, length).TrimEnd();
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<string> ReadRelatedTools(JsonElement element, Func<string, bool> isKnownTool)
    {
        var tools = new List<string>();
        if (!element.TryGetProperty("related_tools", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return tools;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            if (tools.Count >= RelatedToolsMax) break;
            if (item.ValueKind != JsonValueKind.String) continue;

            string? name = item.GetString()?.Trim();
            if (string.IsNullOrEmpty(name) || !isKnownTool(name) || !seen.Add(name)) continue;
            tools.Add(name);
        }
        return tools;
    }
}
