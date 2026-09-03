namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

public class GeneratedQuestionItem
{
    public string QuestionText { get; set; } = string.Empty;
    public string ExpectedPoints { get; set; } = string.Empty;
}

public class GenerationParseResult
{
    public bool Success { get; set; }
    public string? BoardDigest { get; set; }
    public List<GeneratedQuestionItem> Questions { get; set; } = new();
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> DiscardedQuestions { get; set; } = new();
}

public static class BenchmarkGenerationParser
{
    public static GenerationParseResult Parse(string rawText, int requestedCount)
    {
        var result = new GenerationParseResult();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            result.ValidationErrors.Add("Empty response received from generator.");
            return result;
        }

        string cleanJson = StripFencesAndPreamble(rawText);

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(cleanJson);
        }
        catch (JsonException ex)
        {
            // Attempt to salvage questions array if JSON was truncated
            var salvaged = TrySalvageQuestions(cleanJson);
            if (salvaged != null && salvaged.Count > 0)
            {
                ProcessItems(salvaged, requestedCount, result);
                if (result.Questions.Count >= requestedCount && result.ValidationErrors.Count == 0)
                {
                    result.Success = true;
                    return result;
                }
                result.ValidationErrors.Add($"JSON parse error ({ex.Message}), but salvaged {result.Questions.Count} question(s).");
                result.Success = false;
                return result;
            }

            result.ValidationErrors.Add($"JSON parse error: {ex.Message}");
            return result;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.ValidationErrors.Add("Root element must be a JSON object.");
                return result;
            }

            if (root.TryGetProperty("boardDigest", out var digestProp) && digestProp.ValueKind == JsonValueKind.String)
            {
                result.BoardDigest = digestProp.GetString()?.Trim();
                if (result.BoardDigest != null && result.BoardDigest.Length > 2000)
                {
                    result.BoardDigest = result.BoardDigest[..2000];
                }
            }

            if (!root.TryGetProperty("questions", out var questionsProp) || questionsProp.ValueKind != JsonValueKind.Array)
            {
                result.ValidationErrors.Add("Missing or invalid 'questions' array in JSON object.");
                return result;
            }

            var items = new List<GeneratedQuestionItem>();
            foreach (var el in questionsProp.EnumerateArray())
            {
                string qText = el.TryGetProperty("questionText", out var qp) ? qp.GetString() ?? "" : "";
                string exp = el.TryGetProperty("expectedPoints", out var ep) ? ep.GetString() ?? "" : "";
                items.Add(new GeneratedQuestionItem
                {
                    QuestionText = qText.Trim(),
                    ExpectedPoints = exp.Trim()
                });
            }

            ProcessItems(items, requestedCount, result);
            result.Success = result.ValidationErrors.Count == 0 && result.Questions.Count >= requestedCount;
            return result;
        }
    }

    private static void ProcessItems(List<GeneratedQuestionItem> items, int requestedCount, GenerationParseResult result)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.QuestionText) || string.IsNullOrWhiteSpace(item.ExpectedPoints))
            {
                result.DiscardedQuestions.Add($"Empty question text or expected points: '{item.QuestionText}'");
                continue;
            }

            // Layer 2 / Step 9 validation: ExpectedPoints must contain a **BOARD FACTS** section
            if (!item.ExpectedPoints.Contains("**BOARD FACTS**", StringComparison.OrdinalIgnoreCase))
            {
                result.ValidationErrors.Add($"Rejected question '{item.QuestionText}': rubric missing required **BOARD FACTS** section.");
                result.DiscardedQuestions.Add($"Rejected question '{item.QuestionText}': rubric missing required **BOARD FACTS** section.");
                continue;
            }

            if (result.Questions.Count < requestedCount)
            {
                result.Questions.Add(item);
            }
            else
            {
                result.DiscardedQuestions.Add($"Dropped extra question beyond requested count {requestedCount}: '{item.QuestionText}'");
            }
        }

        if (result.Questions.Count < requestedCount)
        {
            result.ValidationErrors.Add($"Shortfall: Generated {result.Questions.Count} question(s) of {requestedCount} requested.");
        }
    }

    private static string StripFencesAndPreamble(string text)
    {
        text = text.Trim();
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            return fenceMatch.Groups[1].Value.Trim();
        }

        int firstBrace = text.IndexOf('{');
        int lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }

        return text;
    }

    private static List<GeneratedQuestionItem>? TrySalvageQuestions(string text)
    {
        var matches = Regex.Matches(text, @"""questionText""\s*:\s*""((?:\\.|[^""\\])*)""\s*,\s*""expectedPoints""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline);
        if (matches.Count == 0) return null;

        var list = new List<GeneratedQuestionItem>();
        foreach (Match m in matches)
        {
            try
            {
                string qText = Regex.Unescape(m.Groups[1].Value);
                string exp = Regex.Unescape(m.Groups[2].Value);
                list.Add(new GeneratedQuestionItem { QuestionText = qText, ExpectedPoints = exp });
            }
            catch
            {
                list.Add(new GeneratedQuestionItem { QuestionText = m.Groups[1].Value, ExpectedPoints = m.Groups[2].Value });
            }
        }
        return list;
    }
}
