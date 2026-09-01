namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

public class BenchmarkDifficultyParsedItem
{
    public long Id { get; set; }
    public int Difficulty { get; set; }
    public string? Rationale { get; set; }
}

public class BenchmarkDifficultyParseResult
{
    public bool Success { get; set; }
    public List<BenchmarkDifficultyParsedItem> Items { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? RawJson { get; set; }
}

public static class BenchmarkDifficultyParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static BenchmarkDifficultyParseResult Parse(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return new BenchmarkDifficultyParseResult
            {
                Success = false,
                ErrorMessage = "Empty or null assessor response."
            };
        }

        string json = StripCodeFences(rawResponse);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var items = new List<BenchmarkDifficultyParsedItem>();

            if (root.TryGetProperty("questions", out var questionsElement) && questionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in questionsElement.EnumerateArray())
                {
                    long id = 0;
                    if (q.TryGetProperty("id", out var idProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.Number)
                        {
                            id = idProp.GetInt64();
                        }
                        else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var parsedId))
                        {
                            id = parsedId;
                        }
                    }

                    int difficulty = 50;
                    if (q.TryGetProperty("difficulty", out var diffProp))
                    {
                        if (diffProp.ValueKind == JsonValueKind.Number)
                        {
                            difficulty = Math.Clamp(diffProp.GetInt32(), 1, 100);
                        }
                        else if (diffProp.ValueKind == JsonValueKind.String && int.TryParse(diffProp.GetString(), out var parsedDiff))
                        {
                            difficulty = Math.Clamp(parsedDiff, 1, 100);
                        }
                    }

                    string? rationale = null;
                    if (q.TryGetProperty("rationale", out var ratProp) && ratProp.ValueKind == JsonValueKind.String)
                    {
                        rationale = ratProp.GetString();
                    }

                    items.Add(new BenchmarkDifficultyParsedItem
                    {
                        Id = id,
                        Difficulty = difficulty,
                        Rationale = rationale
                    });
                }
            }

            return new BenchmarkDifficultyParseResult
            {
                Success = items.Count > 0,
                Items = items,
                RawJson = json,
                ErrorMessage = items.Count > 0 ? null : "No question difficulty ratings parsed from response."
            };
        }
        catch (JsonException jex)
        {
            return new BenchmarkDifficultyParseResult
            {
                Success = false,
                RawJson = json,
                ErrorMessage = $"JSON parsing failed: {jex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new BenchmarkDifficultyParseResult
            {
                Success = false,
                RawJson = json,
                ErrorMessage = $"Unexpected error parsing difficulty response: {ex.Message}"
            };
        }
    }

    private static string StripCodeFences(string text)
    {
        string trimmed = text.Trim();

        var fenceMatch = Regex.Match(trimmed, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            return fenceMatch.Groups[1].Value.Trim();
        }

        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return trimmed;
    }
}
