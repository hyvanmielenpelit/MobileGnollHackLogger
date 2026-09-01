namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public class BenchmarkPerQuestionAssessmentResult
{
    [JsonPropertyName("accuracyLevel")]
    public int AccuracyLevel { get; set; }

    [JsonPropertyName("completenessLevel")]
    public int CompletenessLevel { get; set; }

    [JsonPropertyName("concisenessLevel")]
    public int ConcisenessLevel { get; set; }

    [JsonPropertyName("readabilityLevel")]
    public int ReadabilityLevel { get; set; }

    [JsonPropertyName("criticalError")]
    public bool CriticalError { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public class PerQuestionAssessmentParseResult
{
    public bool Success { get; set; }
    public BenchmarkPerQuestionAssessmentResult? Result { get; set; }
    public string? RawJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BenchmarkSynthesisResult
{
    [JsonPropertyName("finalScore")]
    public int FinalScore { get; set; }

    [JsonPropertyName("strengths")]
    public string Strengths { get; set; } = string.Empty;

    [JsonPropertyName("weaknesses")]
    public string Weaknesses { get; set; } = string.Empty;

    [JsonPropertyName("overallComments")]
    public string OverallComments { get; set; } = string.Empty;
}

public class SynthesisParseResult
{
    public bool Success { get; set; }
    public BenchmarkSynthesisResult? Result { get; set; }
    public string? RawJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AssessmentQuestionVerdict
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("score")]
    public int? Score { get; set; }

    [JsonPropertyName("verdict")]
    public string Verdict { get; set; } = string.Empty;

    [JsonPropertyName("hallucination")]
    public bool Hallucination { get; set; }

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;
}

public class AssessmentVerdictJson
{
    [JsonPropertyName("questions")]
    public List<AssessmentQuestionVerdict> Questions { get; set; } = new();

    [JsonPropertyName("overallScore")]
    public int OverallScore { get; set; }

    [JsonPropertyName("strengths")]
    public string Strengths { get; set; } = string.Empty;

    [JsonPropertyName("weaknesses")]
    public string Weaknesses { get; set; } = string.Empty;

    [JsonPropertyName("overallComments")]
    public string OverallComments { get; set; } = string.Empty;
}

public class AssessmentParseResult
{
    public bool Success { get; set; }
    public AssessmentVerdictJson? Verdict { get; set; }
    public int? ComputedScore { get; set; }
    public string? RawJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class BenchmarkAssessmentParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static PerQuestionAssessmentParseResult ParsePerQuestion(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new PerQuestionAssessmentParseResult
            {
                Success = false,
                ErrorMessage = "Assessment text was empty."
            };
        }

        string json = StripCodeFences(rawText);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int accuracyLevel = GetIntProperty(root, "accuracyLevel", "accuracy", "accuracy_level");
            int completenessLevel = GetIntProperty(root, "completenessLevel", "completeness", "completeness_level");
            int concisenessLevel = GetIntProperty(root, "concisenessLevel", "conciseness", "conciseness_level");
            int readabilityLevel = GetIntProperty(root, "readabilityLevel", "readability", "readability_level");

            bool criticalError = false;
            if (root.TryGetProperty("criticalError", out var ceProp) || root.TryGetProperty("critical_error", out ceProp))
            {
                if (ceProp.ValueKind == JsonValueKind.True || ceProp.ValueKind == JsonValueKind.False)
                {
                    criticalError = ceProp.GetBoolean();
                }
                else if (ceProp.ValueKind == JsonValueKind.String && bool.TryParse(ceProp.GetString(), out var ceBool))
                {
                    criticalError = ceBool;
                }
            }

            string? comment = null;
            if (root.TryGetProperty("comment", out var commentProp) && commentProp.ValueKind == JsonValueKind.String)
            {
                comment = commentProp.GetString();
            }

            var result = new BenchmarkPerQuestionAssessmentResult
            {
                AccuracyLevel = Math.Clamp(accuracyLevel, 0, 6),
                CompletenessLevel = Math.Clamp(completenessLevel, 0, 6),
                ConcisenessLevel = Math.Clamp(concisenessLevel, 0, 6),
                ReadabilityLevel = Math.Clamp(readabilityLevel, 0, 6),
                CriticalError = criticalError,
                Comment = comment
            };

            return new PerQuestionAssessmentParseResult
            {
                Success = true,
                Result = result,
                RawJson = json
            };
        }
        catch (Exception ex)
        {
            return new PerQuestionAssessmentParseResult
            {
                Success = false,
                RawJson = json,
                ErrorMessage = $"JSON parse error: {ex.Message}"
            };
        }
    }

    public static SynthesisParseResult ParseFinalSynthesis(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new SynthesisParseResult
            {
                Success = false,
                ErrorMessage = "Synthesis text was empty."
            };
        }

        string json = StripCodeFences(rawText);

        try
        {
            var result = JsonSerializer.Deserialize<BenchmarkSynthesisResult>(json, JsonOptions);
            if (result == null)
            {
                return new SynthesisParseResult
                {
                    Success = false,
                    RawJson = json,
                    ErrorMessage = "Deserialization of synthesis returned null."
                };
            }

            result.FinalScore = Math.Clamp(result.FinalScore, 1, 100);

            return new SynthesisParseResult
            {
                Success = true,
                Result = result,
                RawJson = json
            };
        }
        catch (Exception ex)
        {
            return new SynthesisParseResult
            {
                Success = false,
                RawJson = json,
                ErrorMessage = $"Synthesis JSON parse error: {ex.Message}"
            };
        }
    }

    public static AssessmentParseResult Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new AssessmentParseResult
            {
                Success = false,
                ErrorMessage = "Assessment text was empty."
            };
        }

        string json = StripCodeFences(rawText);

        try
        {
            var verdict = JsonSerializer.Deserialize<AssessmentVerdictJson>(json, JsonOptions);
            if (verdict == null)
            {
                return new AssessmentParseResult
                {
                    Success = false,
                    RawJson = json,
                    ErrorMessage = "Deserialization returned null."
                };
            }

            var scoredQuestions = verdict.Questions
                .Where(q => !string.Equals(q.Verdict, "excluded", StringComparison.OrdinalIgnoreCase) && q.Score.HasValue)
                .ToList();

            int? computedScore = null;
            if (scoredQuestions.Count > 0)
            {
                double avg = scoredQuestions.Average(q => Math.Clamp(q.Score!.Value, 0, 10));
                computedScore = Math.Clamp((int)Math.Round((avg / 10.0) * 100.0), 1, 100);
            }

            verdict.OverallScore = Math.Clamp(verdict.OverallScore, 1, 100);

            return new AssessmentParseResult
            {
                Success = true,
                Verdict = verdict,
                ComputedScore = computedScore,
                RawJson = json
            };
        }
        catch (Exception ex)
        {
            return new AssessmentParseResult
            {
                Success = false,
                RawJson = json,
                ErrorMessage = $"JSON parse error: {ex.Message}"
            };
        }
    }

    private static int GetIntProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetInt32();
                }
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var val))
                {
                    return val;
                }
            }
        }
        return 0;
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
