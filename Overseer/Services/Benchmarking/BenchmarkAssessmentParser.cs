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

    /// <summary>
    /// The claim the assessor calls a critical error, quoted verbatim from the graded answer.
    /// Required whenever <see cref="CriticalError"/> is true: the cap exists for asserted
    /// falsehoods, and a quote is what distinguishes one from an omission.
    /// </summary>
    [JsonPropertyName("criticalErrorQuote")]
    public string? CriticalErrorQuote { get; set; }

    /// <summary>The rubric point behind the accuracy deduction, or the assessor's own basis.</summary>
    [JsonPropertyName("accuracyEvidence")]
    public string? AccuracyEvidence { get; set; }

    /// <summary>The rubric point behind the completeness deduction, or the assessor's own basis.</summary>
    [JsonPropertyName("completenessEvidence")]
    public string? CompletenessEvidence { get; set; }

    /// <summary>
    /// Claims the answer asserts that the rubric neither states nor contradicts, and that the
    /// assessor could not positively refute. Scoring method v6 forbids deducting ACCURACY for
    /// these; they are recorded so that a claim recurring across unrelated model families can be
    /// told apart from one a single model invented.
    /// </summary>
    [JsonPropertyName("unverifiedClaims")]
    public List<string> UnverifiedClaims { get; set; } = new();

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>
    /// Set when the parser demoted an uncited critical error. Not from the model.
    /// </summary>
    [JsonIgnore]
    public bool CriticalErrorDemoted { get; set; }

    /// <summary>
    /// Unverified claims dropped because they could not be found in the graded answer. Not from
    /// the model; a non-zero value says the assessor is paraphrasing rather than quoting, which
    /// is worth knowing before trusting the recorded claims.
    /// </summary>
    [JsonIgnore]
    public int UnverifiedClaimsDropped { get; set; }

    /// <summary>
    /// The assessor's own prose describes a fabrication while <see cref="CriticalError"/> is
    /// false. Advisory: the parser never promotes a critical error the assessor declined to
    /// declare. See <see cref="BenchmarkVerdictConsistency"/>.
    /// </summary>
    [JsonIgnore]
    public bool ContestedVerdict { get; set; }

    /// <summary>
    /// The assessor docked accuracy or completeness to UnevidencedDeductionMaxLevel or below while
    /// its stated evidence for that dimension names no defect. Advisory; see
    /// <see cref="BenchmarkVerdictConsistency.HasUnevidencedDeduction"/>.
    /// </summary>
    [JsonIgnore]
    public bool UnevidencedDeduction { get; set; }
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

    /// <summary>
    /// Parses one per-question verdict. Pass <paramref name="gradedAnswerText"/> — the exact text
    /// the assessor was shown — to have an uncited critical error demoted: the cap takes a
    /// question to 25 regardless of its levels, and on the 2026-09-03 run it was applied for an
    /// omission, which the rubric explicitly excludes. A claim that cannot be found in the
    /// answer is not a claim the answer made.
    /// </summary>
    public static PerQuestionAssessmentParseResult ParsePerQuestion(string? rawText, string? gradedAnswerText = null)
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

            string? criticalErrorQuote = GetStringProperty(root, "criticalErrorQuote", "critical_error_quote");
            string? accuracyEvidence = GetStringProperty(root, "accuracyEvidence", "accuracy_evidence");
            string? completenessEvidence = GetStringProperty(root, "completenessEvidence", "completeness_evidence");

            bool demoted = false;
            if (criticalError && gradedAnswerText != null && !QuoteAppearsInAnswer(criticalErrorQuote, gradedAnswerText))
            {
                criticalError = false;
                demoted = true;
                string reason = string.IsNullOrWhiteSpace(criticalErrorQuote)
                    ? "no quote was supplied"
                    : "the quoted claim does not appear in the graded answer";
                comment = string.IsNullOrWhiteSpace(comment)
                    ? $"[Harness: critical error not applied — {reason}.]"
                    : $"{comment} [Harness: critical error not applied — {reason}.]";
            }

            // Unverified claims get the same quote verification a critical error gets, for the
            // same reason: a claim that cannot be found in the answer is not a claim the answer
            // made. Dropping it silently would let a paraphrase accumulate across runs and be
            // read later as cross-model corroboration of a rubric gap that nobody actually
            // asserted. The graded answer is not always available (re-parsing a stored verdict),
            // in which case the claims are taken as given.
            var unverifiedClaims = new List<string>();
            int unverifiedClaimsDropped = 0;
            foreach (string claim in GetStringArrayProperty(root, "unverifiedClaims", "unverified_claims"))
            {
                if (gradedAnswerText != null && !QuoteAppearsInAnswer(claim, gradedAnswerText))
                {
                    unverifiedClaimsDropped++;
                    continue;
                }

                unverifiedClaims.Add(claim);
            }

            // Advisory only. The assessor said "hallucinates" and then declined to set the flag;
            // the harness records the divergence and routes it to a second reader rather than
            // overriding a judgement it is not in a position to make.
            bool contestedVerdict = !criticalError &&
                (BenchmarkVerdictConsistency.MentionsFabrication(comment) ||
                 BenchmarkVerdictConsistency.MentionsFabrication(accuracyEvidence) ||
                 BenchmarkVerdictConsistency.MentionsFabrication(completenessEvidence));

            // The inverse of contestedVerdict: there the prose says more than the flag, here it says less.
            // Same treatment — recorded, routed to a second reader, never applied to the score.
            bool unevidencedDeduction = BenchmarkVerdictConsistency.HasUnevidencedDeduction(
                Math.Clamp(accuracyLevel, 0, 6), accuracyEvidence,
                Math.Clamp(completenessLevel, 0, 6), completenessEvidence);

            var result = new BenchmarkPerQuestionAssessmentResult
            {
                AccuracyLevel = Math.Clamp(accuracyLevel, 0, 6),
                CompletenessLevel = Math.Clamp(completenessLevel, 0, 6),
                ConcisenessLevel = Math.Clamp(concisenessLevel, 0, 6),
                ReadabilityLevel = Math.Clamp(readabilityLevel, 0, 6),
                CriticalError = criticalError,
                CriticalErrorQuote = criticalErrorQuote,
                AccuracyEvidence = accuracyEvidence,
                CompletenessEvidence = completenessEvidence,
                UnverifiedClaims = unverifiedClaims,
                Comment = comment,
                CriticalErrorDemoted = demoted,
                UnverifiedClaimsDropped = unverifiedClaimsDropped,
                ContestedVerdict = contestedVerdict,
                UnevidencedDeduction = unevidencedDeduction
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

    private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                string? value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// A string array property, tolerating the two shapes models actually return: a JSON array of
    /// strings, and a bare string where the model had exactly one item and forgot the brackets.
    /// Non-string array entries are skipped rather than failing the parse — one malformed entry
    /// must not cost the whole verdict.
    /// </summary>
    private static List<string> GetStringArrayProperty(JsonElement element, params string[] propertyNames)
    {
        var values = new List<string>();

        foreach (var name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var prop)) continue;

            if (prop.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;

                    string? value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value.Trim());
                    }
                }

                return values;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                string? value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }

                return values;
            }
        }

        return values;
    }

    /// <summary>
    /// Whether the assessor's quote is genuinely present in the graded answer. Whitespace and
    /// case are normalised, because a model re-wraps and re-cases when it quotes; a minimum
    /// length is required, because a two-word "quote" matches any prose and would defeat the
    /// check it exists to perform.
    /// </summary>
    private static bool QuoteAppearsInAnswer(string? quote, string answerText)
    {
        if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(answerText))
        {
            return false;
        }

        string normalizedQuote = NormalizeForQuoteMatch(quote);
        if (normalizedQuote.Length < MinimumCriticalErrorQuoteLength)
        {
            return false;
        }

        return NormalizeForQuoteMatch(answerText).Contains(normalizedQuote, StringComparison.OrdinalIgnoreCase);
    }

    private const int MinimumCriticalErrorQuoteLength = 16;

    private static string NormalizeForQuoteMatch(string text)
    {
        // Markdown emphasis is dropped as well: an assessor quoting "**guaranteed**" as
        // "guaranteed" is quoting the answer, and failing that match would demote a legitimate
        // critical error, which is the expensive direction of this decision.
        string collapsed = Regex.Replace(text, @"[*_`>#]", string.Empty);
        return Regex.Replace(collapsed, @"\s+", " ").Trim();
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
