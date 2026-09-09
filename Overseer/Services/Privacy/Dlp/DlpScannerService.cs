using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Privacy.Dlp;

/// <summary>One kind of sensitive value. Each is an individual switch.</summary>
public enum DlpClass
{
    /// <summary>Provider and cloud API keys: <c>sk-</c>, <c>AIzaSy</c>, <c>AKIA</c>, <c>ghp_</c>, <c>github_pat_</c>.</summary>
    ApiKey,

    /// <summary>PEM and PGP private-key blocks, header through footer.</summary>
    PrivateKey,

    /// <summary>Bearer tokens and JWTs.</summary>
    Token,

    /// <summary>Payment card numbers, Luhn-validated.</summary>
    CreditCard,

    /// <summary>US social security numbers, with area/group/serial validation.</summary>
    Ssn,

    /// <summary>Email addresses.</summary>
    Email,

    /// <summary>Phone numbers.</summary>
    Phone
}

/// <summary>Which classes of sensitive value are masked on the way out to a provider.</summary>
/// <remarks>
/// Masking is a reduction in accidental credential egress, never a guarantee. Detection is
/// shape-based, so a secret with no recognisable shape — an internal password, a bare hex
/// string, a token in a format nobody has published — passes straight through. Treat this as
/// one layer, not as permission to paste credentials into a chat.
/// </remarks>
public sealed record DlpPolicy
{
    public bool ApiKeys { get; init; } = true;

    public bool PrivateKeys { get; init; } = true;

    public bool Tokens { get; init; } = true;

    public bool CreditCards { get; init; } = true;

    public bool Ssns { get; init; } = true;

    /* Off by default: masking these measurably degrades answers for a class of data the user
       usually intended to send. An address book pasted in to be reformatted, or a support
       thread the model is asked to summarise, is unusable once every address is a placeholder. */
    public bool Emails { get; init; } = false;

    public bool PhoneNumbers { get; init; } = false;

    /// <summary>The built-in default: credentials masked, contact details not.</summary>
    public static readonly DlpPolicy Defaults = new();

    /// <summary>Masking fully off. There is no master switch: this is the off state.</summary>
    public static readonly DlpPolicy None = new()
    {
        ApiKeys = false,
        PrivateKeys = false,
        Tokens = false,
        CreditCards = false,
        Ssns = false,
        Emails = false,
        PhoneNumbers = false
    };

    /// <summary>Whether one class is masked under this policy.</summary>
    public bool IsEnabled(DlpClass dlpClass) => dlpClass switch
    {
        DlpClass.ApiKey => ApiKeys,
        DlpClass.PrivateKey => PrivateKeys,
        DlpClass.Token => Tokens,
        DlpClass.CreditCard => CreditCards,
        DlpClass.Ssn => Ssns,
        DlpClass.Email => Emails,
        DlpClass.Phone => PhoneNumbers,
        _ => false
    };

    /// <summary>False when nothing at all is masked, which lets every caller skip the scan.</summary>
    public bool AnyEnabled
        => ApiKeys || PrivateKeys || Tokens || CreditCards || Ssns || Emails || PhoneNumbers;
}

/// <summary>One detected value: where it is, and what it is.</summary>
/// <param name="Class">The class the pattern that found it belongs to.</param>
/// <param name="Start">Offset of the value in the scanned string.</param>
/// <param name="Length">Length of the value.</param>
/// <param name="Value">The matched substring — the secret itself, so never log this.</param>
public readonly record struct DlpFinding(DlpClass Class, int Start, int Length, string Value);

/// <summary>
/// Finds sensitive values in outbound text: the assembled prompt, an attachment's extracted
/// text, or the result of a tool call.
/// </summary>
/// <remarks>
/// Stateless and thread-safe. The scanner only locates values; substituting them for stable
/// placeholders and putting them back afterwards is <see cref="DlpTokenVault"/>'s job.
/// </remarks>
public sealed class DlpScannerService
{
    /// <summary>
    /// Minimum Shannon entropy, in bits per character, for a prefixed key or token to be
    /// treated as real.
    /// </summary>
    /// <remarks>
    /// The threshold trades a small false-negative risk against a large false-positive one.
    /// Documentation, tutorials and configuration templates are full of key-shaped strings —
    /// <c>sk-XXXXXXXXXXXXXXXXXXXX</c>, <c>AIzaSyYOUR_KEY_HERE</c> — and masking those turns a
    /// question about configuration into a question the model cannot read. A genuine key is
    /// random and clears this comfortably: real provider keys measure above 5 bits per
    /// character, so the margin is wide.
    /// </remarks>
    public const double MinKeyEntropyBitsPerChar = 3.0;

    /* Recognises this framework's own placeholders so they can be reserved: a token must never
       be detected as a secret, or a second masking pass would replace it and masking would stop
       being idempotent. No pattern below matches a token today, so this is a guard against a
       future pattern that would. */
    internal static readonly Regex RedactionTokenPattern = new(
        @"\[REDACTED_[A-Z][A-Z_]*_[0-9]+\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /* Placeholder wording that appears in documentation and never in a random credential. The
       entropy gate alone does not reject "AIzaSyYOUR_API_KEY_HERE_XXXXXXXXXXX", which measures
       about 3.4 bits per character because the words and the run of X are still varied enough;
       these two extra checks do. A random 40-character key containing one of these words, or a
       run of five identical characters, is a one-in-a-million event. */
    private static readonly string[] PlaceholderWords =
    {
        "YOUR", "EXAMPLE", "PLACEHOLDER", "REDACTED", "INSERT", "SAMPLE", "DUMMY", "XXXX"
    };

    private const int PlaceholderRunLength = 5;

    private readonly DlpPolicy _floor;

    public DlpScannerService(IConfiguration configuration)
    {
        var section = configuration.GetSection("PrivacySettings:DlpFloor");

        /* Unlike the confidential floor, every value here defaults to false: the operator
           forces nothing, and the built-in defaults come from DlpPolicy instead. Forcing a
           class on is a deliberate act. */
        _floor = new DlpPolicy
        {
            ApiKeys = ReadFlag(section, "ApiKeys"),
            PrivateKeys = ReadFlag(section, "PrivateKeys"),
            Tokens = ReadFlag(section, "Tokens"),
            CreditCards = ReadFlag(section, "CreditCards"),
            Ssns = ReadFlag(section, "Ssns"),
            Emails = ReadFlag(section, "Emails"),
            PhoneNumbers = ReadFlag(section, "PhoneNumbers")
        };
    }

    /// <summary>
    /// Classes the operator forces on regardless of the user's preference. Read-only, and
    /// reported to the client so the UI can disable what it cannot change.
    /// </summary>
    public DlpPolicy Floor => _floor;

    /// <summary>
    /// The stricter of the user's preference and the operator floor: a class is masked if
    /// either asks for it.
    /// </summary>
    /// <param name="settings">The user's settings, or null when they have none.</param>
    /// <remarks>
    /// A null per-class value means "no preference" and resolves to the built-in default, so a
    /// user who has never opened the privacy page still gets credential masking.
    /// </remarks>
    public DlpPolicy Resolve(UserAiSettings? settings) => new()
    {
        ApiKeys = Stricter(settings?.DlpMaskApiKeys, DlpPolicy.Defaults.ApiKeys, _floor.ApiKeys),
        PrivateKeys = Stricter(settings?.DlpMaskPrivateKeys, DlpPolicy.Defaults.PrivateKeys, _floor.PrivateKeys),
        Tokens = Stricter(settings?.DlpMaskTokens, DlpPolicy.Defaults.Tokens, _floor.Tokens),
        CreditCards = Stricter(settings?.DlpMaskCreditCards, DlpPolicy.Defaults.CreditCards, _floor.CreditCards),
        Ssns = Stricter(settings?.DlpMaskSsns, DlpPolicy.Defaults.Ssns, _floor.Ssns),
        Emails = Stricter(settings?.DlpMaskEmails, DlpPolicy.Defaults.Emails, _floor.Emails),
        PhoneNumbers = Stricter(settings?.DlpMaskPhoneNumbers, DlpPolicy.Defaults.PhoneNumbers, _floor.PhoneNumbers)
    };

    /// <summary>
    /// Non-overlapping findings, ordered by position. On overlap the longest match wins.
    /// </summary>
    /// <param name="text">The text to scan. Null and empty return no findings.</param>
    /// <param name="policy">Which classes to look for.</param>
    public IReadOnlyList<DlpFinding> Scan(string? text, DlpPolicy policy)
    {
        if (string.IsNullOrEmpty(text) || !policy.AnyEnabled)
            return Array.Empty<DlpFinding>();

        List<Candidate>? candidates = null;

        for (int order = 0; order < Patterns.Length; order++)
        {
            var pattern = Patterns[order];

            /* The substring pre-filter is what keeps the common case cheap. Almost no message
               contains a credential, and running every pattern over every 50 KB prompt would
               spend the cost on the 99% that has nothing to find. A marker test is a vectorised
               memory scan; a regex sweep is not. */
            if (!policy.IsEnabled(pattern.Class) || !pattern.HasMarker(text))
                continue;

            foreach (Match match in pattern.Regex.Matches(text))
            {
                // A pattern may deliberately match more than it wants replaced — "Bearer " is
                // context, not secret — so the span comes from the "secret" group where present.
                var group = match.Groups["secret"];
                int start = group.Success ? group.Index : match.Index;
                int length = group.Success ? group.Length : match.Length;
                string value = text.Substring(start, length);

                if (pattern.RequireRandomness && !LooksRandom(value))
                    continue;

                if (pattern.Validate != null && !pattern.Validate(value))
                    continue;

                (candidates ??= new List<Candidate>()).Add(
                    new Candidate(pattern.Class, start, length, value, order));
            }
        }

        if (candidates == null)
            return Array.Empty<DlpFinding>();

        var reserved = FindReservedSpans(text);

        candidates.Sort(static (a, b) =>
        {
            int byStart = a.Start.CompareTo(b.Start);
            if (byStart != 0)
                return byStart;

            // Longest wins at the same start; the pattern order breaks a remaining tie so the
            // result does not depend on regex iteration order.
            int byLength = b.Length.CompareTo(a.Length);
            return byLength != 0 ? byLength : a.Order.CompareTo(b.Order);
        });

        var findings = new List<DlpFinding>(candidates.Count);
        int consumedTo = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.Start < consumedTo)
                continue;

            if (reserved != null && OverlapsReserved(reserved, candidate.Start, candidate.Length))
                continue;

            findings.Add(new DlpFinding(candidate.Class, candidate.Start, candidate.Length, candidate.Value));
            consumedTo = candidate.Start + candidate.Length;
        }

        return findings;
    }

    /// <summary>Shannon entropy of a string, in bits per character. Empty is zero.</summary>
    public static double ShannonEntropy(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0.0;

        var counts = new Dictionary<char, int>(value.Length);
        foreach (char c in value)
        {
            counts.TryGetValue(c, out int existing);
            counts[c] = existing + 1;
        }

        double entropy = 0.0;
        double total = value.Length;

        foreach (int count in counts.Values)
        {
            double p = count / total;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    /// <summary>
    /// Luhn check over a candidate card number, ignoring space and hyphen separators.
    /// </summary>
    public static bool IsLuhnValid(string candidate)
    {
        int sum = 0;
        int digits = 0;
        bool doubling = false;

        for (int i = candidate.Length - 1; i >= 0; i--)
        {
            char c = candidate[i];
            if (c == ' ' || c == '-')
                continue;

            if (c < '0' || c > '9')
                return false;

            int digit = c - '0';
            digits++;

            if (doubling)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            doubling = !doubling;
        }

        return digits is >= 13 and <= 19 && sum % 10 == 0;
    }

    private static bool Stricter(bool? user, bool builtIn, bool floor) => (user ?? builtIn) || floor;

    private static bool ReadFlag(IConfigurationSection section, string key)
    {
        /* Parsed by hand rather than with GetValue<bool>, which throws on an unconvertible
           value. A typo in one line of appsettings.json must not take the application down at
           startup; it resolves to the documented default instead. */
        return bool.TryParse(section[key]?.Trim(), out bool value) && value;
    }

    private static bool LooksRandom(string secret)
        => ShannonEntropy(secret) >= MinKeyEntropyBitsPerChar && !LooksLikePlaceholder(secret);

    private static bool LooksLikePlaceholder(string secret)
    {
        foreach (string word in PlaceholderWords)
        {
            if (secret.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        int run = 1;
        for (int i = 1; i < secret.Length; i++)
        {
            run = secret[i] == secret[i - 1] ? run + 1 : 1;
            if (run >= PlaceholderRunLength)
                return true;
        }

        return false;
    }

    private static List<(int Start, int End)>? FindReservedSpans(string text)
    {
        if (!text.Contains("[REDACTED_", StringComparison.Ordinal))
            return null;

        List<(int Start, int End)>? spans = null;
        foreach (Match match in RedactionTokenPattern.Matches(text))
            (spans ??= new List<(int, int)>()).Add((match.Index, match.Index + match.Length));

        return spans;
    }

    private static bool OverlapsReserved(List<(int Start, int End)> reserved, int start, int length)
    {
        int end = start + length;
        foreach (var span in reserved)
        {
            if (start < span.End && span.Start < end)
                return true;
        }

        return false;
    }

    private static bool ContainsDigit(string text)
    {
        foreach (char c in text)
        {
            if (c is >= '0' and <= '9')
                return true;
        }

        return false;
    }

    /// <param name="HasMarker">The cheap substring test that decides whether the regex runs at all.</param>
    /// <param name="RequireRandomness">Whether the match must clear the entropy and placeholder gates.</param>
    /// <param name="Validate">An extra check on the matched value, such as Luhn.</param>
    private sealed record DlpPattern(
        DlpClass Class,
        Regex Regex,
        Func<string, bool> HasMarker,
        bool RequireRandomness = false,
        Func<string, bool>? Validate = null);

    private readonly record struct Candidate(DlpClass Class, int Start, int Length, string Value, int Order);

    private const RegexOptions Standard = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    /* Every pattern is compiled once into this table. Constructing a Regex per call would put
       the parse and the IL emit on the hot path of every turn.

       Order matters only as the final tie-break when two patterns claim the same span at the
       same length: the credential classes come first so a key wrapped in an Authorization
       header is reported as a key rather than as a bearer token. */
    private static readonly DlpPattern[] Patterns =
    {
        // OpenAI and Anthropic style: sk-, sk-proj-, sk-ant-, sk-ant-apiNN-. The trailing class
        // includes '-', so the one pattern covers every prefix variant.
        new(DlpClass.ApiKey,
            new Regex(@"(?<![A-Za-z0-9])sk-[A-Za-z0-9_\-]{20,}", Standard),
            static t => t.Contains("sk-", StringComparison.Ordinal),
            RequireRandomness: true),

        // Google API keys are AIza followed by 35 characters; the length is loosened to 30 so a
        // truncated or template key still reaches the entropy gate rather than being missed.
        new(DlpClass.ApiKey,
            new Regex(@"(?<![A-Za-z0-9])AIza[A-Za-z0-9_\-]{30,}", Standard),
            static t => t.Contains("AIza", StringComparison.Ordinal),
            RequireRandomness: true),

        new(DlpClass.ApiKey,
            new Regex(@"(?<![A-Za-z0-9])AKIA[A-Z0-9]{16}(?![A-Za-z0-9])", Standard),
            static t => t.Contains("AKIA", StringComparison.Ordinal),
            RequireRandomness: true),

        new(DlpClass.ApiKey,
            new Regex(@"(?<![A-Za-z0-9])ghp_[A-Za-z0-9]{30,}", Standard),
            static t => t.Contains("ghp_", StringComparison.Ordinal),
            RequireRandomness: true),

        new(DlpClass.ApiKey,
            new Regex(@"(?<![A-Za-z0-9])github_pat_[A-Za-z0-9_]{22,}", Standard),
            static t => t.Contains("github_pat_", StringComparison.Ordinal),
            RequireRandomness: true),

        /* The whole block, header through footer, so the token replaces the key rather than
           just its first line. An unterminated block matches to the end of input: a truncated
           private key is still a leaked private key. */
        new(DlpClass.PrivateKey,
            new Regex(
                @"-----BEGIN (?:[A-Z ]*PRIVATE KEY|PGP PRIVATE KEY BLOCK)-----"
                + @".*?(?:-----END (?:[A-Z ]*PRIVATE KEY|PGP PRIVATE KEY BLOCK)-----|\z)",
                Standard | RegexOptions.Singleline),
            static t => t.Contains("-----BEGIN", StringComparison.Ordinal)),

        // A JWT: three base64url segments, the first the base64 of a JSON header, which always
        // starts "eyJ".
        new(DlpClass.Token,
            new Regex(
                @"(?<![A-Za-z0-9._\-])eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]{4,}",
                Standard),
            static t => t.Contains("eyJ", StringComparison.Ordinal),
            RequireRandomness: true),

        /* Only the token is the secret, so "Bearer" itself stays in the text: masking the
           keyword would leave the model unable to see that it is looking at an auth header, and
           the header shape is exactly the context that makes the rest of the message readable.
           The separator is spaces and tabs rather than \s so the match cannot run past the end
           of the line onto whatever follows. */
        new(DlpClass.Token,
            new Regex(
                @"(?<![A-Za-z0-9])Bearer[ \t]+(?<secret>[A-Za-z0-9._~+/=\-]{16,})",
                Standard | RegexOptions.IgnoreCase),
            static t => t.Contains("Bearer", StringComparison.OrdinalIgnoreCase),
            RequireRandomness: true),

        /* 13 to 19 digits with single spaces or hyphens tolerated as group separators, and a
           non-alphanumeric boundary at each end so a longer identifier is not chopped into a
           card-shaped piece. Luhn then rejects the overwhelming majority of digit runs that
           happen to be the right length. */
        new(DlpClass.CreditCard,
            new Regex(@"(?<![0-9A-Za-z_])[0-9](?:[ \-]?[0-9]){12,18}(?![0-9A-Za-z_])", Standard),
            static t => ContainsDigit(t),
            Validate: IsLuhnValid),

        /* Real SSN validation rather than \d{3}-\d{2}-\d{4}: the area may not be 000, 666 or
           900-999, the group may not be 00, and the serial may not be 0000. Without those the
           pattern masks version strings and part numbers. */
        new(DlpClass.Ssn,
            new Regex(
                @"(?<![0-9A-Za-z_\-])(?!000|666|9)[0-9]{3}-(?!00)[0-9]{2}-(?!0000)[0-9]{4}(?![0-9A-Za-z_\-])",
                Standard),
            static t => ContainsDigit(t)),

        // The bare nine-digit form, which needs the boundaries to be worth anything at all.
        new(DlpClass.Ssn,
            new Regex(
                @"(?<![0-9A-Za-z_\-])(?!000|666|9)[0-9]{3}(?!00)[0-9]{2}(?!0000)[0-9]{4}(?![0-9A-Za-z_\-])",
                Standard),
            static t => ContainsDigit(t)),

        new(DlpClass.Email,
            new Regex(
                @"(?<![A-Za-z0-9._%+\-])[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+(?![A-Za-z0-9\-])",
                Standard),
            static t => t.Contains('@', StringComparison.Ordinal)),

        /* Two shapes only: an international number introduced by '+', and the North American
           three-three-four with mandatory separators. A bare run of digits is deliberately not
           a phone number here — that reading would swallow dates, ids and quantities. */
        new(DlpClass.Phone,
            new Regex(
                @"(?<![0-9A-Za-z])(?:"
                + @"\+[0-9]{1,3}[ .\-]?(?:\([0-9]{1,4}\)[ .\-]?)?[0-9]{2,4}(?:[ .\-]?[0-9]{2,4}){1,3}"
                + @"|\(?[0-9]{3}\)?[ .\-][0-9]{3}[ .\-][0-9]{4}"
                + @")(?![0-9])",
                Standard),
            static t => ContainsDigit(t))
    };
}
