namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public enum BenchmarkClaimVerdict
{
    Indeterminate = 0,
    Supported = 1,
    Refuted = 2
}

public record BenchmarkClaimVerification(
    [property: JsonPropertyName("claimIndex")] int ClaimIndex,
    [property: JsonPropertyName("claim")] string Claim,
    [property: JsonPropertyName("verdict")]
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    BenchmarkClaimVerdict Verdict,
    [property: JsonPropertyName("citation")] string? Citation,
    [property: JsonPropertyName("basis")] string? Basis);

public class BenchmarkClaimVerificationParseResult
{
    public bool Success { get; set; }
    public IReadOnlyList<BenchmarkClaimVerification> Verifications { get; set; } = Array.Empty<BenchmarkClaimVerification>();
    public int ClaimsSupportedCount { get; set; }
    public int ClaimsRefutedCount { get; set; }
    public int ClaimsIndeterminateCount { get; set; }
    public int MismatchesDropped { get; set; }
    public int CitationsMissingDemoted { get; set; }
    public string? RawJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class BenchmarkClaimVerificationParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static BenchmarkClaimVerificationParseResult Parse(
        string? responseText,
        IReadOnlyList<string> submittedClaims)
    {
        submittedClaims ??= Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new BenchmarkClaimVerificationParseResult
            {
                Success = false,
                ErrorMessage = "Verification text was empty."
            };
        }

        string json = StripCodeFences(responseText);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement arrayElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     (root.TryGetProperty("verifications", out arrayElement) ||
                      root.TryGetProperty("Verifications", out arrayElement)))
            {
                if (arrayElement.ValueKind != JsonValueKind.Array)
                {
                    return new BenchmarkClaimVerificationParseResult
                    {
                        Success = false,
                        RawJson = json,
                        ErrorMessage = "'verifications' property was not a JSON array."
                    };
                }
            }
            else
            {
                return new BenchmarkClaimVerificationParseResult
                {
                    Success = false,
                    RawJson = json,
                    ErrorMessage = "Could not find 'verifications' array in JSON object."
                };
            }

            var matched = new BenchmarkClaimVerification?[submittedClaims.Count];
            int mismatchesDropped = 0;
            int citationsMissingDemoted = 0;

            foreach (var el in arrayElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                int claimIndex = -1;
                if (el.TryGetProperty("claimIndex", out var ci) ||
                    el.TryGetProperty("claim_index", out ci) ||
                    el.TryGetProperty("index", out ci))
                {
                    if (ci.ValueKind == JsonValueKind.Number && ci.TryGetInt32(out int ciNum))
                    {
                        claimIndex = ciNum;
                    }
                    else if (ci.ValueKind == JsonValueKind.String && int.TryParse(ci.GetString(), out int ciParsed))
                    {
                        claimIndex = ciParsed;
                    }
                }

                string? claimText = GetStringProperty(el, "claim", "text");
                string? citation = GetStringProperty(el, "citation", "source");
                string? basis = GetStringProperty(el, "basis", "explanation", "reason");

                BenchmarkClaimVerdict verdict = BenchmarkClaimVerdict.Indeterminate;
                if (el.TryGetProperty("verdict", out var vProp) || el.TryGetProperty("Verdict", out vProp))
                {
                    if (vProp.ValueKind == JsonValueKind.String)
                    {
                        string? vStr = vProp.GetString();
                        if (string.Equals(vStr, "Supported", StringComparison.OrdinalIgnoreCase))
                        {
                            verdict = BenchmarkClaimVerdict.Supported;
                        }
                        else if (string.Equals(vStr, "Refuted", StringComparison.OrdinalIgnoreCase))
                        {
                            verdict = BenchmarkClaimVerdict.Refuted;
                        }
                        else
                        {
                            verdict = BenchmarkClaimVerdict.Indeterminate;
                        }
                    }
                    else if (vProp.ValueKind == JsonValueKind.Number && vProp.TryGetInt32(out int vNum))
                    {
                        verdict = vNum switch
                        {
                            1 => BenchmarkClaimVerdict.Supported,
                            2 => BenchmarkClaimVerdict.Refuted,
                            _ => BenchmarkClaimVerdict.Indeterminate
                        };
                    }
                }

                // Match entry to submitted claim by claimIndex, then verify echoed claim equals submitted claim.
                if (claimIndex < 0 || claimIndex >= submittedClaims.Count)
                {
                    mismatchesDropped++;
                    continue;
                }

                if (!ClaimsMatch(claimText, submittedClaims[claimIndex]))
                {
                    mismatchesDropped++;
                    continue;
                }

                // Demote Supported or Refuted with a blank citation to Indeterminate.
                if ((verdict == BenchmarkClaimVerdict.Supported || verdict == BenchmarkClaimVerdict.Refuted) &&
                    string.IsNullOrWhiteSpace(citation))
                {
                    verdict = BenchmarkClaimVerdict.Indeterminate;
                    citationsMissingDemoted++;
                    basis = string.IsNullOrWhiteSpace(basis)
                        ? "[Harness: demoted to Indeterminate — missing citation.]"
                        : $"{basis} [Harness: demoted to Indeterminate — missing citation.]";
                }

                matched[claimIndex] = new BenchmarkClaimVerification(
                    claimIndex,
                    submittedClaims[claimIndex],
                    verdict,
                    citation,
                    basis);
            }

            // Submitted claims absent from response default to Indeterminate.
            for (int i = 0; i < submittedClaims.Count; i++)
            {
                if (matched[i] == null)
                {
                    matched[i] = new BenchmarkClaimVerification(
                        i,
                        submittedClaims[i],
                        BenchmarkClaimVerdict.Indeterminate,
                        null,
                        "[Harness: absent from verifier response; defaulted to Indeterminate.]");
                }
            }

            var finalVerifications = matched.Select(m => m!).ToList();

            return new BenchmarkClaimVerificationParseResult
            {
                Success = true,
                Verifications = finalVerifications,
                ClaimsSupportedCount = finalVerifications.Count(v => v.Verdict == BenchmarkClaimVerdict.Supported),
                ClaimsRefutedCount = finalVerifications.Count(v => v.Verdict == BenchmarkClaimVerdict.Refuted),
                ClaimsIndeterminateCount = finalVerifications.Count(v => v.Verdict == BenchmarkClaimVerdict.Indeterminate),
                MismatchesDropped = mismatchesDropped,
                CitationsMissingDemoted = citationsMissingDemoted,
                RawJson = json
            };
        }
        catch (Exception ex)
        {
            return new BenchmarkClaimVerificationParseResult
            {
                Success = false,
                RawJson = json,
                ErrorMessage = $"Verification JSON parse error: {ex.Message}"
            };
        }
    }

    private static bool ClaimsMatch(string? echoed, string submitted)
    {
        if (string.IsNullOrWhiteSpace(echoed)) return false;
        string normEchoed = NormalizeClaim(echoed);
        string normSubmitted = NormalizeClaim(submitted);
        return string.Equals(normEchoed, normSubmitted, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeClaim(string text)
    {
        string unquoted = text.Trim().Trim('"', '\'');
        return Regex.Replace(unquoted, @"\s+", " ");
    }

    private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (string name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private static string StripCodeFences(string text)
    {
        string trimmed = text.Trim();

        var fenceMatch = Regex.Match(trimmed, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            return fenceMatch.Groups[1].Value.Trim();
        }

        int firstBracket = trimmed.IndexOf('[');
        int lastBracket = trimmed.LastIndexOf(']');
        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');

        if (firstBracket >= 0 && lastBracket > firstBracket && (firstBrace < 0 || firstBracket < firstBrace))
        {
            return trimmed.Substring(firstBracket, lastBracket - firstBracket + 1);
        }

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return trimmed;
    }
}
