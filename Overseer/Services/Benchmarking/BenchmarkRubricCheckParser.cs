namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

public class RubricCheckParseResult
{
    public bool Success { get; set; }
    public string Verdict { get; set; } = "supported";
    public List<RubricCheckFinding> Findings { get; set; } = new();
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> DiscardedFindings { get; set; } = new();
}

public static class BenchmarkRubricCheckParser
{
    public static RubricCheckParseResult Parse(string rawText, string rubricText)
    {
        var result = new RubricCheckParseResult();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            result.ValidationErrors.Add("Empty response received from rubric checker.");
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

            string rawVerdict = root.TryGetProperty("verdict", out var vp) && vp.ValueKind == JsonValueKind.String
                ? vp.GetString()?.Trim().ToLowerInvariant() ?? "supported"
                : "supported";

            result.Verdict = rawVerdict == "unsupported" ? "unsupported" : "supported";

            if (root.TryGetProperty("findings", out var findingsProp) && findingsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in findingsProp.EnumerateArray())
                {
                    string claim = el.TryGetProperty("claim", out var cp) ? cp.GetString() ?? "" : "";
                    string assessment = el.TryGetProperty("assessment", out var ap)
                        ? ap.GetString() ?? ""
                        : (el.TryGetProperty("verdict", out var vp2) ? vp2.GetString() ?? "" : "");
                    string? boardQuote = el.TryGetProperty("boardQuote", out var bq) && bq.ValueKind == JsonValueKind.String ? bq.GetString() : null;

                    claim = claim.Trim();
                    assessment = assessment.Trim().ToLowerInvariant();

                    if (string.IsNullOrWhiteSpace(claim)) continue;

                    // Verifiable-quote discipline:
                    // If assessment is contradicted, unsupported, or not-in-board, the claim MUST appear in the rubric text.
                    // If the checker fabricated or hallucinated a claim not in the rubric, discard it!
                    if ((assessment == "contradicted" || assessment == "unsupported" || assessment == "not-in-board") &&
                        !rubricText.Contains(claim, StringComparison.OrdinalIgnoreCase))
                    {
                        result.DiscardedFindings.Add($"Discarded finding claiming '{claim}' is {assessment} because claim text is not in the rubric.");
                        continue;
                    }

                    result.Findings.Add(new RubricCheckFinding
                    {
                        Claim = claim,
                        Assessment = assessment,
                        BoardQuote = boardQuote
                    });
                }
            }

            // If after discarding hallucinated claims, no claims are contradicted or not-in-board,
            // set verdict to supported!
            bool hasContradictionOrMissing = false;
            foreach (var f in result.Findings)
            {
                if (f.Assessment == "contradicted" || f.Assessment == "not-in-board")
                {
                    hasContradictionOrMissing = true;
                    break;
                }
            }

            if (!hasContradictionOrMissing && result.Verdict == "unsupported")
            {
                result.Verdict = "supported";
            }

            result.Success = true;
            return result;
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
}
