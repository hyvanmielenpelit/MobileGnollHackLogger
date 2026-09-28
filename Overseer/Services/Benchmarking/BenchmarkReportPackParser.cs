namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Overseer.Models;

public sealed class BenchmarkReportParseResult
{
    public bool Success { get; init; }
    public BenchmarkReportWriterOutput? Output { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Reads the report-pack writer's answer into a <see cref="BenchmarkReportWriterOutput"/>.
///
/// <para>Parsing is tolerant of shape and strict only about the JSON itself: property names match
/// case-insensitively, unknown properties are ignored, a null or missing list is empty, a question
/// number may be written as <c>7</c>, <c>"7"</c> or <c>"Q7"</c>, and a list element written as a bare
/// string becomes an item with that text. Section keys equal to a known slot id apart from case are
/// written with the slot id's casing, and <c>for</c> and <c>triage</c> are lower-cased. A number that
/// cannot be read becomes <c>0</c>, which no exam contains, so the validator reports it rather than
/// the parser hiding it. Whether the result satisfies the document rules is
/// <see cref="BenchmarkReportPackValidator"/>'s concern.</para>
/// </summary>
public static class BenchmarkReportPackParser
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly string[] KnownSlots =
    {
        BenchmarkReportSlots.Meaning,
        BenchmarkReportSlots.Confidence,
        BenchmarkReportSlots.Abstract,
        BenchmarkReportSlots.WhyItScored,
        BenchmarkReportSlots.WhatWorked,
        BenchmarkReportSlots.OverseerChat,
        BenchmarkReportSlots.BenchmarkSystem,
        BenchmarkReportSlots.ModelResult,
    };

    /// <summary>Never throws: every failure is a result with <see cref="BenchmarkReportParseResult.Error"/> set.</summary>
    public static BenchmarkReportParseResult Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return Fail("The writer returned an empty answer.");
        }

        try
        {
            string text = BenchmarkAnswerSanitizer.StripThoughts(rawText);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Fail("The writer's answer held only thoughts and no JSON.");
            }

            string json = BenchmarkJsonExtractor.Extract(text);
            using var document = JsonDocument.Parse(json, DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail($"The writer's answer is a JSON {root.ValueKind.ToString().ToLowerInvariant()}, not an object.");
            }

            return new BenchmarkReportParseResult { Success = true, Output = ReadOutput(root) };
        }
        catch (JsonException ex)
        {
            return Fail($"The writer's answer is not valid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Fail($"The writer's answer could not be read: {ex.Message}");
        }
    }

    private static BenchmarkReportParseResult Fail(string error)
        => new() { Success = false, Error = error };

    private static BenchmarkReportWriterOutput ReadOutput(JsonElement root)
    {
        var output = new BenchmarkReportWriterOutput
        {
            Headline = TryGet(root, "headline", out var headline) ? ReadString(headline) : string.Empty
        };

        if (TryGet(root, "sections", out var sections) && sections.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in sections.EnumerateObject())
            {
                string key = NormalizeSlot(property.Name);
                string value = ReadString(property.Value);
                if (!output.Sections.TryGetValue(key, out string? existing) || string.IsNullOrWhiteSpace(existing))
                {
                    output.Sections[key] = value;
                }
            }
        }

        output.Strengths = ReadList(root, "strengths", e => ReadItem(e, new BenchmarkReportWriterItem()), t => new BenchmarkReportWriterItem { Text = t });
        output.Weaknesses = ReadList(root, "weaknesses", e => ReadItem(e, new BenchmarkReportWriterItem()), t => new BenchmarkReportWriterItem { Text = t });
        output.Recommendations = ReadList(
            root,
            "recommendations",
            e =>
            {
                var item = ReadItem(e, new BenchmarkReportWriterRecommendation());
                item.For = TryGet(e, "for", out var target) ? ReadString(target).ToLowerInvariant() : string.Empty;
                return item;
            },
            t => new BenchmarkReportWriterRecommendation { Text = t });
        output.QuestionTopics = ReadList(
            root,
            "questionTopics",
            e => new BenchmarkReportQuestionTopic
            {
                Question = TryGet(e, "question", out var q) ? ReadInt(q) : 0,
                Topic = TryGet(e, "topic", out var topic) ? ReadString(topic) : string.Empty
            },
            t => new BenchmarkReportQuestionTopic { Topic = t });
        output.QuestionNotes = ReadList(
            root,
            "questionNotes",
            e => new BenchmarkReportQuestionNote
            {
                Question = TryGet(e, "question", out var q) ? ReadInt(q) : 0,
                Note = TryGet(e, "note", out var note) ? ReadString(note) : string.Empty
            },
            t => new BenchmarkReportQuestionNote { Note = t });
        output.Leads = ReadList(
            root,
            "leads",
            e =>
            {
                var item = ReadItem(e, new BenchmarkReportLead());
                item.Triage = TryGet(e, "triage", out var triage) ? ReadString(triage).ToLowerInvariant() : string.Empty;
                return item;
            },
            t => new BenchmarkReportLead { Text = t });

        return output;
    }

    private static T ReadItem<T>(JsonElement element, T item) where T : BenchmarkReportWriterItem
    {
        item.Text = TryGet(element, "text", out var text) ? ReadString(text) : string.Empty;
        item.Questions = TryGet(element, "questions", out var questions) ? ReadIntList(questions) : new List<int>();
        item.Evidence = TryGet(element, "evidence", out var evidence) ? ReadStringList(evidence) : new List<string>();
        return item;
    }

    private static List<T> ReadList<T>(JsonElement root, string name, Func<JsonElement, T> fromObject, Func<string, T> fromString)
    {
        var list = new List<T>();
        if (!TryGet(root, name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                list.Add(fromObject(element));
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                list.Add(fromString(ReadString(element)));
            }
        }

        return list;
    }

    /// <summary>The first property whose name matches, ignoring case.</summary>
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeSlot(string key)
    {
        string trimmed = key.Trim();
        return KnownSlots.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    /// <summary>A string as written; a number or boolean as its JSON text; an array of strings as paragraphs; anything else empty.</summary>
    private static string ReadString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => (element.GetString() ?? string.Empty).Trim(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(
            "\n\n",
            element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => (e.GetString() ?? string.Empty).Trim())
                .Where(s => s.Length > 0)),
        _ => string.Empty
    };

    private static List<string> ReadStringList(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .Where(e => e.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    .Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty).Trim() : e.GetRawText())
                    .Where(s => s.Length > 0)
                    .ToList();
            case JsonValueKind.String:
                return SplitList(element.GetString()).ToList();
            default:
                return new List<string>();
        }
    }

    private static List<int> ReadIntList(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .Where(e => e.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                    .Select(ReadInt)
                    .ToList();
            case JsonValueKind.Number:
                return new List<int> { ReadInt(element) };
            case JsonValueKind.String:
                return SplitList(element.GetString()).Select(ParseQuestionNumber).ToList();
            default:
                return new List<int>();
        }
    }

    private static int ReadInt(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt32(out int n)) return n;
                if (element.TryGetDouble(out double d) && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue) return (int)d;
                return 0;
            case JsonValueKind.String:
                return ParseQuestionNumber(element.GetString());
            default:
                return 0;
        }
    }

    /// <summary><c>7</c>, <c>Q7</c>, <c>q7</c> or <c>#7</c> is 7; anything else is 0.</summary>
    private static int ParseQuestionNumber(string? text)
    {
        string s = (text ?? string.Empty).Trim();
        if (s.StartsWith('Q') || s.StartsWith('q') || s.StartsWith('#'))
        {
            s = s.Substring(1).Trim();
        }

        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    private static IEnumerable<string> SplitList(string? text)
        => (text ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
