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
    [property: JsonPropertyName("basis")] string? Basis)
{
    /// <summary>
    /// Why the harness submitted this item (<see cref="BenchmarkClaimRoles"/>), stamped by the harness
    /// from its own submission manifest and never read from model output. Null on a record stored
    /// before harness 31, which consumers read by exact-text matching instead.
    /// </summary>
    [JsonPropertyName("roles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Roles { get; init; }

    /// <summary>
    /// Set by <see cref="BenchmarkCitationLivenessCheck"/> when the cited source function has no live
    /// call site. The stored <see cref="Verdict"/> is left as the verifier gave it; every flag and
    /// count reads <see cref="EffectiveVerdict"/>.
    /// </summary>
    [JsonPropertyName("citationNote")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CitationNote { get; init; }

    /// <summary>
    /// True on an unverified claim the assessor recorded as <c>Suspected false: </c>; <see cref="Claim"/>
    /// is then the answer's sentence alone, as the verifier received it. Null otherwise.
    /// </summary>
    [JsonPropertyName("suspectedFalse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SuspectedFalse { get; init; }

    /// <summary>The assessor's reason for a suspected-false claim, the text after its em dash. Null otherwise.</summary>
    [JsonPropertyName("suspicion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Suspicion { get; init; }

    /// <summary>
    /// The <c>UnverifiedClaimsJson</c> entry this item was submitted for, verbatim, when it differs from
    /// <see cref="Claim"/> (a suspected-false entry). Null otherwise.
    /// </summary>
    [JsonPropertyName("recordedClaim")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordedClaim { get; init; }

    /// <summary>
    /// On an accused sentence: the spans the assessor quoted, as they occur in the answer. <see cref="Claim"/>
    /// is the sentence or list item that encloses them. Null otherwise.
    /// </summary>
    [JsonPropertyName("quotedFragments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? QuotedFragments { get; init; }

    /// <summary>On an accused sentence: the assessor's evidence sentence(s) quoting it. Null otherwise.</summary>
    [JsonPropertyName("charge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Charge { get; init; }

    /// <summary>The verdict every flag and count reads: Indeterminate when a <see cref="CitationNote"/> is set.</summary>
    [JsonIgnore]
    public BenchmarkClaimVerdict EffectiveVerdict
        => string.IsNullOrWhiteSpace(CitationNote) ? Verdict : BenchmarkClaimVerdict.Indeterminate;
}

/// <summary>
/// The reasons an item is submitted to the claim verifier. <see cref="UnverifiedClaim"/> is a claim
/// of the answer; the others are the assessor's statements or accusations, checked to test the
/// assessor rather than the answer.
/// </summary>
public static class BenchmarkClaimRoles
{
    public const string UnverifiedClaim = "unverifiedClaim";
    public const string CriticalErrorQuote = "criticalErrorQuote";
    public const string OutOfRubricBasis = "outOfRubricBasis";
    public const string AccusedQuote = "accusedQuote";

    /// <summary>A sentence of the assessor's own accuracy evidence or comment; Supported means the assessor was right.</summary>
    public const string AssessorStatement = "assessorStatement";

    /// <summary>A claim of the answer's own: a legacy record without roles, or one carrying <see cref="UnverifiedClaim"/>.</summary>
    public static bool IsOrdinaryClaim(BenchmarkClaimVerification verification)
        => verification.Roles == null || verification.Roles.Contains(UnverifiedClaim);

    public static bool HasRole(BenchmarkClaimVerification verification, string role)
        => verification.Roles != null && verification.Roles.Contains(role);

    /// <summary>True when any record in the list carries roles, which makes roles, not text, the rule for the whole list.</summary>
    public static bool HasRoles(IReadOnlyList<BenchmarkClaimVerification>? verifications)
        => verifications != null && verifications.Any(v => v.Roles != null);
}

/// <summary>
/// An <c>unverifiedClaims</c> entry of the form <c>Suspected false: &lt;sentence&gt; — &lt;reason&gt;</c>:
/// a sentence of the answer the assessor believes false from its own knowledge. The entry is stored
/// verbatim; the verifier receives the sentence alone.
/// </summary>
public static class BenchmarkSuspectedFalseClaim
{
    public const string Prefix = "Suspected false: ";

    private static readonly Regex PrefixRegex = new(
        @"^\s*suspected\s+false\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReasonSeparatorRegex = new(@"\s*—\s*|\s+–\s+|\s+--\s+", RegexOptions.Compiled);

    private static readonly Regex QuoteNormalizationRegex = new(@"[*_`>#]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

    public static bool IsSuspectedFalse(string? entry)
        => !string.IsNullOrWhiteSpace(entry) && PrefixRegex.IsMatch(entry);

    /// <summary>
    /// Splits a suspected-false entry into the answer's sentence and the assessor's reason. The
    /// sentence may itself hold a dash, so with <paramref name="answerText"/> the longest head that
    /// occurs in the answer (emphasis and whitespace ignored) wins; without it, or when no head
    /// occurs, the split is at the last separator. Surrounding quotation marks are removed. False
    /// when the entry lacks the prefix or leaves an empty sentence.
    /// </summary>
    public static bool TryParse(string? entry, string? answerText, out string sentence, out string? reason)
    {
        sentence = entry?.Trim() ?? string.Empty;
        reason = null;
        if (string.IsNullOrWhiteSpace(entry)) return false;

        var prefix = PrefixRegex.Match(entry);
        if (!prefix.Success) return false;

        string rest = entry.Substring(prefix.Length).Trim();
        var separators = ReasonSeparatorRegex.Matches(rest).Cast<Match>().ToList();

        string? head = null;
        string? tail = null;
        if (!string.IsNullOrWhiteSpace(answerText))
        {
            string normalizedAnswer = Normalize(answerText);
            if (Occurs(Unquote(rest), normalizedAnswer))
            {
                head = rest;
            }
            else
            {
                for (int i = separators.Count - 1; i >= 0; i--)
                {
                    string candidate = rest.Substring(0, separators[i].Index);
                    if (Occurs(Unquote(candidate), normalizedAnswer))
                    {
                        head = candidate;
                        tail = rest.Substring(separators[i].Index + separators[i].Length);
                        break;
                    }
                }
            }
        }

        if (head == null)
        {
            if (separators.Count > 0)
            {
                var last = separators[^1];
                head = rest.Substring(0, last.Index);
                tail = rest.Substring(last.Index + last.Length);
            }
            else
            {
                head = rest;
            }
        }

        string trimmedHead = Unquote(head);
        if (trimmedHead.Length == 0) return false;

        sentence = trimmedHead;
        reason = string.IsNullOrWhiteSpace(tail) ? null : tail.Trim();
        return true;
    }

    private static bool Occurs(string text, string normalizedAnswer)
    {
        string normalized = Normalize(text);
        return normalized.Length > 0 && normalizedAnswer.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string text)
        => WhitespaceRunRegex.Replace(QuoteNormalizationRegex.Replace(text, string.Empty), " ").Trim();

    private static string Unquote(string text)
    {
        string t = text.Trim();
        if (t.Length >= 2
            && ((t[0] == '"' && t[^1] == '"') || (t[0] == '“' && t[^1] == '”') || (t[0] == '\'' && t[^1] == '\'')))
        {
            t = t.Substring(1, t.Length - 2).Trim();
        }
        return t;
    }
}

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
    public string? RawResponse { get; set; }
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
                RawResponse = responseText,
                ErrorMessage = "Verification text was empty."
            };
        }

        string json = BenchmarkJsonExtractor.Extract(responseText);

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
                        RawResponse = BenchmarkAssessmentFailure.Truncate(responseText, 8000),
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
                    RawResponse = BenchmarkAssessmentFailure.Truncate(responseText, 8000),
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
                RawResponse = BenchmarkAssessmentFailure.Truncate(responseText, 8000),
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
}
