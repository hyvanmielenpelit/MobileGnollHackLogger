using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Overseer.Services.Providers;

public class ReasoningTextSanitizer
{
    private readonly StringBuilder _buffer = new();
    private bool _inRule2Scan = false;
    private int _rule2MarkerStart = -1;

    // Whether a Markdown fenced code block is open at the head of _buffer. Nothing inside a
    // fence is ever treated as a transport artifact: a fenced block is authored content, and
    // it is the one place a model legitimately shows raw tool-call JSON to a reader.
    private bool _fenceOpen = false;

    private static readonly Regex ControlTokenRegex = new(@"<\|[a-zA-Z_]{1,32}\|>", RegexOptions.Compiled);

    // Any namespaced routing marker, not just `to=functions.<name>`. GPT-family models also
    // emit `to=multi_tool_use.parallel` when a parallel tool batch leaks into the text
    // channel, and pinning the namespace let that form through untouched.
    private static readonly Regex ToolRoutingMarkerRegex =
        new(@"to=[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+", RegexOptions.Compiled);

    // A bare tool-batch payload, with or without a preceding routing marker.
    private static readonly Regex ToolUsesPayloadRegex =
        new(@"\{\s*""tool_uses""\s*:", RegexOptions.Compiled);

    // Channel literals observed between a routing marker and its JSON payload. `json` and
    // `commentary` were already handled; the rest come from the 2026-09-03 GPT-5.6 Luna run.
    private static readonly string[] ChannelLiterals =
    {
        "commentary code",
        "commentary",
        "code:",
        "code",
        "/json",
        "json"
    };

    // The same literals wrapped in parentheses, e.g. `(json)` or `(commentary code)`.
    private static readonly Regex BracketedChannelLiteralRegex =
        new(@"^\(\s*(?:json|code|commentary(?:\s+code)?)\s*\)", RegexOptions.Compiled);

    public string Push(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return string.Empty;

        _buffer.Append(delta);
        return ProcessBuffer(isFlush: false);
    }

    public string Flush()
    {
        return ProcessBuffer(isFlush: true);
    }

    public static string SanitizeStateless(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sanitizer = new ReasoningTextSanitizer();
        var result = sanitizer.Push(text) + sanitizer.Flush();
        return result;
    }

    /// <summary>
    /// Finds the end of the last leaked tool-call artifact in <paramref name="text"/> — a routing
    /// marker with the payload that follows it, or a bare tool-batch payload — ignoring anything
    /// inside a fenced code block. Returns false when the text carries no such artifact.
    ///
    /// The caller uses this as a classification boundary: a model cannot have authored answer
    /// prose *before* attempting a tool call, so everything up to this point is analysis-channel
    /// content even though it arrived on the text channel.
    /// </summary>
    public static bool TryFindLastLeakedArtifactEnd(string? text, out int end)
    {
        end = -1;
        if (string.IsNullOrEmpty(text)) return false;

        var fenceState = new ReasoningTextSanitizer();

        foreach (Match m in ToolRoutingMarkerRegex.Matches(text))
        {
            if (fenceState.IsInsideFenceFromStart(text, m.Index)) continue;

            int candidate = m.Index + m.Length;
            int probe = candidate;
            bool advanced = true;
            while (advanced && probe < text.Length)
            {
                advanced = false;
                while (probe < text.Length && char.IsWhiteSpace(text[probe])) { probe++; advanced = true; }
                if (probe >= text.Length) break;

                var ct = ControlTokenRegex.Match(text, probe);
                if (ct.Success && ct.Index == probe) { probe += ct.Length; advanced = true; continue; }

                int lit = MatchChannelLiteralAt(text, probe);
                if (lit > 0) { probe += lit; advanced = true; continue; }
            }

            if (probe < text.Length && text[probe] == '{')
            {
                int objectEnd = FindBalancedObjectEnd(text, probe, out bool bailout);
                if (!bailout && objectEnd != -1)
                {
                    candidate = objectEnd + 1;
                }
            }

            if (candidate > end) end = candidate;
        }

        foreach (Match m in ToolUsesPayloadRegex.Matches(text))
        {
            if (fenceState.IsInsideFenceFromStart(text, m.Index)) continue;

            int objectEnd = FindBalancedObjectEnd(text, m.Index, out bool bailout);
            if (bailout || objectEnd == -1) continue;
            if (objectEnd + 1 > end) end = objectEnd + 1;
        }

        return end > 0;
    }

    private bool IsInsideFenceFromStart(string text, int index)
    {
        _fenceOpen = false;
        return IsInsideFence(text, index);
    }

    private string ProcessBuffer(bool isFlush)
    {
        var output = new StringBuilder();

        while (_buffer.Length > 0)
        {
            if (_inRule2Scan)
            {
                // In rule 2 brace scan mode
                string current = _buffer.ToString();
                int markerStart = _rule2MarkerStart;

                // Output any clean prefix before the marker start
                if (markerStart > 0)
                {
                    output.Append(SanitizeCharactersAndControlTokens(current.Substring(0, markerStart)));
                    _buffer.Remove(0, markerStart);
                    current = _buffer.ToString();
                    markerStart = 0;
                    _rule2MarkerStart = 0;
                }

                var match = ToolRoutingMarkerRegex.Match(current);
                if (!match.Success || match.Index != 0)
                {
                    // Marker was lost or moved; bail out and reset
                    _inRule2Scan = false;
                    _rule2MarkerStart = -1;
                    continue;
                }

                int afterMarker = match.Length;
                // Skip optional whitespace, control tokens, and leaked channel literals
                // ("json", "code:", "(commentary code)", ...) between the marker and its payload.
                int jsonStart = afterMarker;
                bool advanced = true;
                while (advanced && jsonStart < current.Length)
                {
                    advanced = false;

                    while (jsonStart < current.Length && char.IsWhiteSpace(current[jsonStart]))
                    {
                        jsonStart++;
                        advanced = true;
                    }

                    if (jsonStart >= current.Length) break;

                    var ctMatch = ControlTokenRegex.Match(current, jsonStart);
                    if (ctMatch.Success && ctMatch.Index == jsonStart)
                    {
                        jsonStart += ctMatch.Length;
                        advanced = true;
                        continue;
                    }

                    int literalLength = MatchChannelLiteralAt(current, jsonStart);
                    if (literalLength > 0)
                    {
                        jsonStart += literalLength;
                        advanced = true;
                        continue;
                    }
                }

                if (!isFlush && jsonStart < current.Length && current[jsonStart] != '{')
                {
                    string remaining = current.Substring(jsonStart);
                    if (IsPrefixOfControlToken(remaining) ||
                        IsPrefixOfChannelLiteral(remaining))
                    {
                        break; // Hold back in buffer to wait for potential control token/literal
                    }
                }

                if (jsonStart >= current.Length)
                {
                    // Waiting for more characters to see if a JSON object '{' starts
                    if (isFlush)
                    {
                        // End of stream reached: strip the tool routing marker and release whitespace
                        _buffer.Clear();
                        _inRule2Scan = false;
                        _rule2MarkerStart = -1;
                        break;
                    }
                    // Hold back in buffer
                    break;
                }

                if (current[jsonStart] != '{')
                {
                    // Not a JSON object directly following the marker.
                    // Strip the marker itself, and let the rest process
                    _buffer.Remove(0, afterMarker);
                    _inRule2Scan = false;
                    _rule2MarkerStart = -1;
                    continue;
                }

                // Balanced brace scan starting at jsonStart. Shared with the bare-payload
                // rule so both paths use one set of bail-out guards.
                int jsonEnd = FindBalancedObjectEnd(current, jsonStart, out bool bailout);

                if (bailout)
                {
                    // Bail out: release the held text intact (including marker)
                    output.Append(SanitizeCharactersAndControlTokens(current));
                    _buffer.Clear();
                    _inRule2Scan = false;
                    _rule2MarkerStart = -1;
                    break;
                }

                if (jsonEnd != -1)
                {
                    // Balanced JSON object found! Strip marker + JSON object completely.
                    _buffer.Remove(0, jsonEnd + 1);
                    _inRule2Scan = false;
                    _rule2MarkerStart = -1;
                    continue;
                }
                else
                {
                    // Incomplete JSON object
                    if (isFlush)
                    {
                        // On flush with incomplete/unbalanced JSON, release the held text intact
                        output.Append(SanitizeCharactersAndControlTokens(current));
                        _buffer.Clear();
                        _inRule2Scan = false;
                        _rule2MarkerStart = -1;
                        break;
                    }
                    // Wait for more deltas
                    break;
                }
            }
            else
            {
                // Normal state
                string current = _buffer.ToString();

                // 1. Check for Rule 2: tool routing marker
                var r2Match = ToolRoutingMarkerRegex.Match(current);
                if (r2Match.Success)
                {
                    if (IsInsideFence(current, r2Match.Index))
                    {
                        // Authored content inside a fenced code block. Release it verbatim,
                        // marker included, and resume scanning after it.
                        EmitVerbatim(output, current, r2Match.Index + r2Match.Length);
                        continue;
                    }

                    // We found a rule 2 marker.
                    _inRule2Scan = true;
                    _rule2MarkerStart = r2Match.Index;
                    continue; // Will handle at top of loop
                }

                // 1b. A bare tool-batch payload with no routing marker in front of it. The
                // marker is what normally identifies a leak, but the model sometimes emits
                // only the payload, and `tool_uses` is not a shape authored prose produces.
                var batchMatch = ToolUsesPayloadRegex.Match(current);
                if (batchMatch.Success && !IsInsideFence(current, batchMatch.Index))
                {
                    if (batchMatch.Index > 0)
                    {
                        EmitVerbatim(output, current, batchMatch.Index);
                        continue;
                    }

                    int payloadEnd = FindBalancedObjectEnd(current, 0, out bool payloadBailout);
                    if (payloadEnd != -1)
                    {
                        _buffer.Remove(0, payloadEnd + 1);
                        continue;
                    }

                    if (payloadBailout || isFlush)
                    {
                        // Malformed or unterminated: release intact rather than risk eating
                        // authored text.
                        output.Append(SanitizeCharactersAndControlTokens(current));
                        _buffer.Clear();
                        break;
                    }

                    // Wait for the rest of the payload.
                    break;
                }

                // 2. Check for Rule 1: control tokens matching <|[a-zA-Z_]{1,32}|>
                var r1Match = ControlTokenRegex.Match(current);
                if (r1Match.Success)
                {
                    if (IsInsideFence(current, r1Match.Index))
                    {
                        EmitVerbatim(output, current, r1Match.Index + r1Match.Length);
                        continue;
                    }

                    // Emit everything before the match
                    if (r1Match.Index > 0)
                    {
                        output.Append(SanitizeCharacters(current.Substring(0, r1Match.Index)));
                        UpdateFenceState(current.AsSpan(0, r1Match.Index));
                    }
                    // Remove up to end of control token
                    _buffer.Remove(0, r1Match.Index + r1Match.Length);
                    continue;
                }

                // 3. Check for potential partial prefixes at the tail if not flushing
                if (!isFlush)
                {
                    int holdBackLen = GetViablePrefixLength(current);
                    if (holdBackLen > 0)
                    {
                        int emitLen = current.Length - holdBackLen;
                        if (emitLen > 0)
                        {
                            output.Append(SanitizeCharacters(current.Substring(0, emitLen)));
                            UpdateFenceState(current.AsSpan(0, emitLen));
                            _buffer.Remove(0, emitLen);
                        }
                        break; // Held-back tail remains in _buffer
                    }
                }

                // No viable prefixes or we are flushing; emit all
                output.Append(SanitizeCharacters(current));
                UpdateFenceState(current.AsSpan());
                _buffer.Clear();
                break;
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Emits <paramref name="length"/> characters of <paramref name="current"/> unchanged apart
    /// from character-level sanitisation, keeping the fence state in step, and drops them from
    /// the buffer. Used to release content that must not be scanned for artifacts.
    /// </summary>
    private void EmitVerbatim(StringBuilder output, string current, int length)
    {
        if (length <= 0) return;

        length = Math.Min(length, current.Length);
        output.Append(SanitizeCharacters(current.Substring(0, length)));
        UpdateFenceState(current.AsSpan(0, length));
        _buffer.Remove(0, length);
    }

    /// <summary>
    /// Whether <paramref name="index"/> falls inside a fenced code block, given the fence state
    /// at the head of the buffer.
    /// </summary>
    private bool IsInsideFence(string text, int index)
    {
        bool open = _fenceOpen;
        foreach (int _ in FenceTogglePositions(text.AsSpan(0, Math.Min(index, text.Length))))
        {
            open = !open;
        }
        return open;
    }

    private void UpdateFenceState(ReadOnlySpan<char> emitted)
    {
        foreach (int _ in FenceTogglePositions(emitted))
        {
            _fenceOpen = !_fenceOpen;
        }
    }

    /// <summary>
    /// Positions of Markdown code fences (three or more backticks at the start of a line) in
    /// <paramref name="text"/>. Each one toggles fence state.
    /// </summary>
    private static IEnumerable<int> FenceTogglePositions(ReadOnlySpan<char> text)
    {
        var positions = new List<int>();
        bool atLineStart = true;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                atLineStart = true;
                continue;
            }

            if (atLineStart && (c == ' ' || c == '\t' || c == '\r'))
            {
                // Leading indentation does not end the line-start position.
                continue;
            }

            if (atLineStart && c == '`')
            {
                int run = 0;
                while (i + run < text.Length && text[i + run] == '`') run++;
                if (run >= 3)
                {
                    positions.Add(i);
                    i += run - 1;
                    atLineStart = false;
                    continue;
                }
            }

            atLineStart = false;
        }

        return positions;
    }

    /// <summary>
    /// Length of a leaked channel literal at <paramref name="index"/>, or 0. A literal only
    /// counts when it is followed by whitespace, a control token, a brace, or end of input, so
    /// a word that merely starts with "code" is not consumed.
    /// </summary>
    private static int MatchChannelLiteralAt(string text, int index)
    {
        var bracketed = BracketedChannelLiteralRegex.Match(text.Substring(index));
        if (bracketed.Success)
        {
            return bracketed.Length;
        }

        foreach (var literal in ChannelLiterals)
        {
            if (index + literal.Length > text.Length) continue;
            if (!string.Equals(text.Substring(index, literal.Length), literal, StringComparison.Ordinal)) continue;

            int after = index + literal.Length;
            if (after == text.Length ||
                char.IsWhiteSpace(text[after]) ||
                text[after] == '<' ||
                text[after] == '{')
            {
                return literal.Length;
            }
        }

        return 0;
    }

    private static bool IsPrefixOfChannelLiteral(string suffix)
    {
        if (suffix.Length == 0) return false;

        if ("(commentary code)".StartsWith(suffix, StringComparison.Ordinal) ||
            "(json)".StartsWith(suffix, StringComparison.Ordinal) ||
            "(code)".StartsWith(suffix, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var literal in ChannelLiterals)
        {
            if (literal.StartsWith(suffix, StringComparison.Ordinal) && suffix.Length < literal.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scans a balanced JSON object starting at <paramref name="start"/>. Returns the index of
    /// the closing brace, or -1 when the object is incomplete. <paramref name="bailout"/> is set
    /// when the scan hit a guard (4096-char cap, a blank line, or brace underflow), meaning the
    /// text is not a well-formed payload and must be released intact.
    /// </summary>
    private static int FindBalancedObjectEnd(string text, int start, out bool bailout)
    {
        bailout = false;
        int openBraces = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            if (i - start >= 4096)
            {
                bailout = true;
                return -1;
            }

            if (i + 1 < text.Length && text[i] == '\n' && text[i + 1] == '\n')
            {
                bailout = true;
                return -1;
            }

            char c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '{') openBraces++;
            else if (c == '}')
            {
                openBraces--;
                if (openBraces == 0) return i;
                if (openBraces < 0)
                {
                    bailout = true;
                    return -1;
                }
            }
        }

        return -1;
    }

    private static int GetViablePrefixLength(string text)
    {
        // Max check length up to 32 characters from the end
        int maxCheck = Math.Min(text.Length, 32);

        for (int len = maxCheck; len >= 1; len--)
        {
            string suffix = text.Substring(text.Length - len);

            // Is suffix a prefix of a control token `<|...`?
            if (IsPrefixOfControlToken(suffix))
            {
                return len;
            }

            // Is suffix a prefix of a namespaced routing marker such as `to=functions.`?
            if (IsPrefixOfToolRoutingMarker(suffix))
            {
                return len;
            }

            // Is suffix a partial code fence? Emitting one or two backticks before knowing
            // whether a third follows would decide fence state on incomplete information.
            if (IsPartialFence(suffix))
            {
                return len;
            }
        }

        return 0;
    }

    private static bool IsPartialFence(string suffix)
    {
        if (suffix.Length is not (1 or 2)) return false;

        foreach (char c in suffix)
        {
            if (c != '`') return false;
        }

        return true;
    }

    private static bool IsPrefixOfControlToken(string suffix)
    {
        if (!suffix.StartsWith("<")) return false;
        if (suffix == "<") return true;
        if (!suffix.StartsWith("<|")) return false;
        if (suffix == "<|") return true;

        // Check if characters after `<|` are valid token chars `[a-zA-Z_]`
        for (int i = 2; i < suffix.Length; i++)
        {
            char c = suffix[i];
            if (c == '|' && i == suffix.Length - 1) return true; // `<|abc|` is a prefix of `<|abc|>`
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_'))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Whether <paramref name="suffix"/> could still grow into a namespaced routing marker such
    /// as `to=functions.foo` or `to=multi_tool_use.parallel`. Matches the generalised shape
    /// `to=<ident>(.<ident>)+`, so any namespace is held back, not just `functions`.
    /// </summary>
    private static bool IsPrefixOfToolRoutingMarker(string suffix)
    {
        const string prefix = "to=";
        if (prefix.StartsWith(suffix, StringComparison.Ordinal)) return true;
        if (!suffix.StartsWith(prefix, StringComparison.Ordinal)) return false;

        // Everything after `to=` must still look like a (possibly unfinished) dotted
        // identifier: no empty segments, no characters outside [A-Za-z0-9_].
        bool segmentStart = true;
        for (int i = prefix.Length; i < suffix.Length; i++)
        {
            char c = suffix[i];

            if (c == '.')
            {
                if (segmentStart) return false; // `to=.` or `to=a..`
                segmentStart = true;
                continue;
            }

            bool identChar = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_'
                             || (!segmentStart && c >= '0' && c <= '9');
            if (!identChar) return false;

            segmentStart = false;
        }

        return true;
    }

    private static string SanitizeCharactersAndControlTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var stripped = ControlTokenRegex.Replace(text, "");
        return SanitizeCharacters(stripped);
    }

    private static string SanitizeCharacters(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            // Rule 3: Strip U+FFFD (replacement char) and private use area U+E000..U+F8FF
            if (c == '\uFFFD' || (c >= '\uE000' && c <= '\uF8FF'))
            {
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
