using System;
using System.Collections.Generic;
using System.Text;

namespace Overseer.Services.Privacy.Dlp;

/// <summary>
/// The secret-to-placeholder mapping for one outbound turn: masks text on its way to a
/// provider, and puts the real values back on the way to the browser and to the database.
/// </summary>
/// <remarks>
/// A turn masks in several places — replayed history, the new user message, each attachment's
/// extracted text, and every tool result — and one vault spans all of them, because the model
/// must see one placeholder per secret and not one per occurrence.
///
/// What this buys is a reduction in accidental credential egress, not a guarantee. Detection is
/// shape-based, so a secret the scanner does not recognise is sent verbatim.
/// </remarks>
public sealed class DlpTokenVault
{
    private static readonly Dictionary<DlpClass, string> ClassLabels = new()
    {
        [DlpClass.ApiKey] = "API_KEY",
        [DlpClass.PrivateKey] = "PRIVATE_KEY",
        [DlpClass.Token] = "TOKEN",
        [DlpClass.CreditCard] = "CARD",
        [DlpClass.Ssn] = "SSN",
        [DlpClass.Email] = "EMAIL",
        [DlpClass.Phone] = "PHONE"
    };

    private static readonly Dictionary<DlpClass, string> ClassDescriptions = new()
    {
        [DlpClass.ApiKey] = "provider or cloud API key",
        [DlpClass.PrivateKey] = "private key block",
        [DlpClass.Token] = "bearer token or JWT",
        [DlpClass.CreditCard] = "payment card number",
        [DlpClass.Ssn] = "US social security number",
        [DlpClass.Email] = "email address",
        [DlpClass.Phone] = "phone number"
    };

    /* An ordinal past 999999999 is unreachable in one turn, so this is a true upper bound on
       the token shape "[REDACTED_" + label + "_" + ordinal + "]". */
    private const int MaxOrdinalDigits = 9;

    private static readonly int TokenLengthBound = ComputeTokenLengthBound();

    /* A turn runs its tool calls in parallel, and each of them masks its own result, so two
       threads can be allocating tokens at the same moment. Without the lock two callers could
       mint two ordinals for the same secret, which is the one failure this class exists to
       prevent. */
    private readonly object _gate = new();

    private readonly DlpScannerService _scanner;
    private readonly DlpPolicy _policy;

    // Keyed on the secret itself, deliberately ignoring its class: if the same string were once
    // matched as an API key and once as a bearer token, two entries would mean two tokens.
    private readonly Dictionary<string, string> _tokenBySecret = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _secretByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DlpClass> _classByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<DlpClass, int> _ordinals = new();
    private readonly List<string> _issueOrder = new();

    public DlpTokenVault(DlpScannerService scanner, DlpPolicy policy)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>True while nothing has been masked, which is the common case.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
                return _issueOrder.Count == 0;
        }
    }

    /// <summary>How many distinct secrets this vault holds — occurrences are not counted.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _issueOrder.Count;
        }
    }

    /// <summary>
    /// The longest token this vault could ever produce, for the stream unmasker's window.
    /// </summary>
    /// <remarks>
    /// An upper bound on the token shape rather than the longest token issued so far. Tool
    /// results are masked while the reply is already streaming, so a token minted after the
    /// unmasker started would otherwise widen a window that had been sized too small.
    /// </remarks>
    public int MaxTokenLength => TokenLengthBound;

    /// <summary>Token to class. Never exposes a secret, so this is safe for a debug view.</summary>
    public IReadOnlyDictionary<string, DlpClass> Tokens
    {
        get
        {
            // A snapshot: the live dictionary can gain entries from a parallel tool call while
            // a caller is enumerating it.
            lock (_gate)
                return new Dictionary<string, DlpClass>(_classByToken, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Replaces every enabled class of secret with its stable token, and returns the input
    /// unchanged when nothing matched. Null passes through.
    /// </summary>
    /// <remarks>
    /// Calling this several times in one turn is the normal case, and the same secret yields
    /// the same token every time. Numbering per occurrence instead would show the model
    /// <c>[REDACTED_API_KEY_1]</c> and <c>[REDACTED_API_KEY_2]</c> for one string, and it would
    /// then reason about them as two different credentials — "the first key is invalid, try the
    /// second" — which is worse than not masking at all.
    /// </remarks>
    public string? Mask(string? text)
    {
        if (string.IsNullOrEmpty(text) || !_policy.AnyEnabled)
            return text;

        var findings = _scanner.Scan(text, _policy);
        if (findings.Count == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        int cursor = 0;

        lock (_gate)
        {
            foreach (var finding in findings)
            {
                builder.Append(text, cursor, finding.Start - cursor);
                builder.Append(TokenForLocked(finding.Class, finding.Value));
                cursor = finding.Start + finding.Length;
            }
        }

        builder.Append(text, cursor, text.Length - cursor);
        return builder.ToString();
    }

    /// <summary>
    /// Restores every secret this vault knows, however many times each token occurs. A
    /// token-shaped string the vault never issued is left exactly as it is. Null passes through.
    /// </summary>
    public string? Unmask(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("[REDACTED_", StringComparison.Ordinal))
            return text;

        lock (_gate)
        {
            if (_secretByToken.Count == 0)
                return text;

            return DlpScannerService.RedactionTokenPattern.Replace(
                text,
                match => _secretByToken.TryGetValue(match.Value, out string? secret) ? secret : match.Value);
        }
    }

    /// <summary>
    /// One line per token, naming what was replaced but never the value.
    /// </summary>
    /// <remarks>
    /// Safe to log and safe to stream to the user's own browser: the point is to tell them that
    /// something was withheld from the provider, without repeating it.
    /// </remarks>
    public IReadOnlyList<string> Describe()
    {
        lock (_gate)
        {
            var lines = new List<string>(_issueOrder.Count);
            foreach (string token in _issueOrder)
                lines.Add($"{token} — {ClassDescriptions[_classByToken[token]]}");

            return lines;
        }
    }

    private string TokenForLocked(DlpClass dlpClass, string secret)
    {
        if (_tokenBySecret.TryGetValue(secret, out string? existing))
            return existing;

        _ordinals.TryGetValue(dlpClass, out int previous);
        int ordinal = previous + 1;
        _ordinals[dlpClass] = ordinal;

        string token = $"[REDACTED_{ClassLabels[dlpClass]}_{ordinal}]";

        _tokenBySecret[secret] = token;
        _secretByToken[token] = secret;
        _classByToken[token] = dlpClass;
        _issueOrder.Add(token);

        return token;
    }

    private static int ComputeTokenLengthBound()
    {
        int longestLabel = 0;
        foreach (string label in ClassLabels.Values)
            longestLabel = Math.Max(longestLabel, label.Length);

        // "[REDACTED_" + label + "_" + ordinal + "]"
        return "[REDACTED_".Length + longestLabel + 1 + MaxOrdinalDigits + 1;
    }
}
