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
    /// call site, and by <see cref="BenchmarkClaimVerificationParser"/> when a charged part was not
    /// judged separately. The stored <see cref="Verdict"/> is left as the verifier gave it; every flag
    /// and count reads <see cref="EffectiveVerdict"/>.
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

    /// <summary>
    /// True on an item submitted with a charged part (a <c>Charged part</c> line in its claim block).
    /// Its <see cref="Verdict"/> is then the verifier's <c>chargedPartVerdict</c>, and
    /// <see cref="Citation"/> and <see cref="Basis"/> its <c>chargedPartBasis</c>, which carries the
    /// citation; the verdict, citation and basis the verifier gave the whole item are kept in
    /// <see cref="ItemVerdict"/>, <see cref="ItemCitation"/> and <see cref="ItemBasis"/>. When the
    /// charged part was not judged, <see cref="Verdict"/>, <see cref="Citation"/> and
    /// <see cref="Basis"/> are the item's own and <see cref="CitationNote"/> says so. Null otherwise.
    /// </summary>
    [JsonPropertyName("chargedPart")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ChargedPart { get; init; }

    /// <summary>On a <see cref="ChargedPart"/> item: the verdict the verifier gave the whole item, as text. Null otherwise.</summary>
    [JsonPropertyName("itemVerdict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemVerdict { get; init; }

    /// <summary>On a <see cref="ChargedPart"/> item: the citation the verifier gave the whole item. Null otherwise.</summary>
    [JsonPropertyName("itemCitation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemCitation { get; init; }

    /// <summary>On a <see cref="ChargedPart"/> item: the basis the verifier gave the whole item. Null otherwise.</summary>
    [JsonPropertyName("itemBasis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemBasis { get; init; }

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

    /// <summary>The <see cref="BenchmarkClaimVerification.CitationNote"/> of a charged-part item whose <c>chargedPartVerdict</c> is missing or unparseable.</summary>
    public const string ChargedPartNotJudgedNote = "the charged part was not judged separately";

    private const string MissingCitationDemotion = "[Harness: demoted to Indeterminate — missing citation.]";

    /// <summary>
    /// What a <c>chargedPartBasis</c> must name to cite anything: a <c>src/</c> or <c>include/</c>
    /// file, a wiki page or a board line.
    /// </summary>
    private static readonly Regex ChargedPartCitationRegex = new(
        @"(?<![\w/.-])(?:src|include)/[\w./-]+?\.(?:c|h)\b|\bwiki\s*:|\bboard\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <param name="chargedPartItems">
    /// Per submitted claim, whether it was submitted with a charged part
    /// (<see cref="BenchmarkClaimVerificationPrompt.ChargedPartItems"/>). Such an item's verdict is
    /// read from <c>chargedPartVerdict</c> and its citation from <c>chargedPartBasis</c>; see
    /// <see cref="BenchmarkClaimVerification.ChargedPart"/>. Null when none was.
    /// </param>
    public static BenchmarkClaimVerificationParseResult Parse(
        string? responseText,
        IReadOnlyList<string> submittedClaims,
        IReadOnlyList<bool>? chargedPartItems = null)
    {
        submittedClaims ??= Array.Empty<string>();
        bool IsChargedPartItem(int index) => chargedPartItems != null && index >= 0 && index < chargedPartItems.Count && chargedPartItems[index];

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

                if (!TryGetVerdict(el, out BenchmarkClaimVerdict verdict, "verdict", "Verdict"))
                {
                    verdict = BenchmarkClaimVerdict.Indeterminate;
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

                // The item's own verdict is kept as given; the charged part's verdict is the one read.
                if (IsChargedPartItem(claimIndex))
                {
                    if (!TryGetVerdict(el, out BenchmarkClaimVerdict partVerdict, "chargedPartVerdict", "charged_part_verdict"))
                    {
                        matched[claimIndex] = new BenchmarkClaimVerification(
                            claimIndex,
                            submittedClaims[claimIndex],
                            verdict,
                            citation,
                            basis)
                        {
                            ChargedPart = true,
                            ItemVerdict = verdict.ToString(),
                            ItemCitation = citation,
                            ItemBasis = basis,
                            CitationNote = ChargedPartNotJudgedNote
                        };
                        continue;
                    }

                    string? partBasis = GetStringProperty(el, "chargedPartBasis", "charged_part_basis");

                    // A charged-part basis that names no source file, wiki page or board line cites nothing.
                    if ((partVerdict == BenchmarkClaimVerdict.Supported || partVerdict == BenchmarkClaimVerdict.Refuted) &&
                        (string.IsNullOrWhiteSpace(partBasis) || !ChargedPartCitationRegex.IsMatch(partBasis)))
                    {
                        partVerdict = BenchmarkClaimVerdict.Indeterminate;
                        citationsMissingDemoted++;
                        partBasis = string.IsNullOrWhiteSpace(partBasis)
                            ? MissingCitationDemotion
                            : $"{partBasis} {MissingCitationDemotion}";
                    }

                    matched[claimIndex] = new BenchmarkClaimVerification(
                        claimIndex,
                        submittedClaims[claimIndex],
                        partVerdict,
                        partVerdict == BenchmarkClaimVerdict.Indeterminate ? null : partBasis,
                        partBasis)
                    {
                        ChargedPart = true,
                        ItemVerdict = verdict.ToString(),
                        ItemCitation = citation,
                        ItemBasis = basis
                    };
                    continue;
                }

                // Demote Supported or Refuted with a blank citation to Indeterminate.
                if ((verdict == BenchmarkClaimVerdict.Supported || verdict == BenchmarkClaimVerdict.Refuted) &&
                    string.IsNullOrWhiteSpace(citation))
                {
                    verdict = BenchmarkClaimVerdict.Indeterminate;
                    citationsMissingDemoted++;
                    basis = string.IsNullOrWhiteSpace(basis)
                        ? MissingCitationDemotion
                        : $"{basis} {MissingCitationDemotion}";
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
                        "[Harness: absent from verifier response; defaulted to Indeterminate.]")
                    {
                        ChargedPart = IsChargedPartItem(i) ? true : null
                    };
                }
            }

            var finalVerifications = matched.Select(m => m!).ToList();

            return new BenchmarkClaimVerificationParseResult
            {
                Success = true,
                Verifications = finalVerifications,
                ClaimsSupportedCount = finalVerifications.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Supported),
                ClaimsRefutedCount = finalVerifications.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted),
                ClaimsIndeterminateCount = finalVerifications.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Indeterminate),
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

    /// <summary>
    /// The verdict in the first of <paramref name="propertyNames"/> the element carries: the strings
    /// Supported, Refuted and Indeterminate in any casing, or the numbers 1, 2 and 0. False when no
    /// such property is present or its value is none of these.
    /// </summary>
    private static bool TryGetVerdict(JsonElement element, out BenchmarkClaimVerdict verdict, params string[] propertyNames)
    {
        verdict = BenchmarkClaimVerdict.Indeterminate;
        foreach (string name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var prop)) continue;

            if (prop.ValueKind == JsonValueKind.String)
            {
                string? text = prop.GetString();
                if (string.Equals(text, "Supported", StringComparison.OrdinalIgnoreCase))
                {
                    verdict = BenchmarkClaimVerdict.Supported;
                    return true;
                }
                if (string.Equals(text, "Refuted", StringComparison.OrdinalIgnoreCase))
                {
                    verdict = BenchmarkClaimVerdict.Refuted;
                    return true;
                }
                return string.Equals(text, "Indeterminate", StringComparison.OrdinalIgnoreCase);
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int number))
            {
                switch (number)
                {
                    case 0:
                        return true;
                    case 1:
                        verdict = BenchmarkClaimVerdict.Supported;
                        return true;
                    case 2:
                        verdict = BenchmarkClaimVerdict.Refuted;
                        return true;
                }
            }

            return false;
        }

        return false;
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
