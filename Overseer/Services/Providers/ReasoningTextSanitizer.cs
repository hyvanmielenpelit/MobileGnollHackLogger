using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Overseer.Services.Providers;

public class ReasoningTextSanitizer
{
    private readonly StringBuilder _buffer = new();
    private bool _inRule2Scan = false;
    private int _rule2MarkerStart = -1;

    private static readonly Regex ControlTokenRegex = new(@"<\|[a-zA-Z_]{1,32}\|>", RegexOptions.Compiled);
    private static readonly Regex ToolRoutingMarkerRegex = new(@"to=functions\.[A-Za-z0-9_]+", RegexOptions.Compiled);

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
                // Skip optional whitespace after marker
                int jsonStart = afterMarker;
                while (jsonStart < current.Length && char.IsWhiteSpace(current[jsonStart]))
                {
                    jsonStart++;
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

                // Balanced brace scan starting at jsonStart
                int openBraces = 0;
                bool inString = false;
                bool escaped = false;
                int jsonEnd = -1;
                bool bailout = false;

                for (int i = jsonStart; i < current.Length; i++)
                {
                    // Bailout check 1: accumulation limit (4096 chars from jsonStart)
                    if (i - jsonStart >= 4096)
                    {
                        bailout = true;
                        break;
                    }

                    // Bailout check 2: \n\n encountered
                    if (i + 1 < current.Length && current[i] == '\n' && current[i + 1] == '\n')
                    {
                        bailout = true;
                        break;
                    }

                    char c = current[i];

                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (c == '\\')
                        {
                            escaped = true;
                        }
                        else if (c == '"')
                        {
                            inString = false;
                        }
                    }
                    else
                    {
                        if (c == '"')
                        {
                            inString = true;
                        }
                        else if (c == '{')
                        {
                            openBraces++;
                        }
                        else if (c == '}')
                        {
                            openBraces--;
                            if (openBraces == 0)
                            {
                                jsonEnd = i;
                                break;
                            }
                            if (openBraces < 0)
                            {
                                // Brace underflow
                                bailout = true;
                                break;
                            }
                        }
                    }
                }

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
                    // We found a rule 2 marker.
                    _inRule2Scan = true;
                    _rule2MarkerStart = r2Match.Index;
                    continue; // Will handle at top of loop
                }

                // 2. Check for Rule 1: control tokens matching <|[a-zA-Z_]{1,32}|>
                var r1Match = ControlTokenRegex.Match(current);
                if (r1Match.Success)
                {
                    // Emit everything before the match
                    if (r1Match.Index > 0)
                    {
                        output.Append(SanitizeCharacters(current.Substring(0, r1Match.Index)));
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
                            _buffer.Remove(0, emitLen);
                        }
                        break; // Held-back tail remains in _buffer
                    }
                }

                // No viable prefixes or we are flushing; emit all
                output.Append(SanitizeCharacters(current));
                _buffer.Clear();
                break;
            }
        }

        return output.ToString();
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

            // Is suffix a prefix of `to=functions.`?
            if (IsPrefixOfToolRoutingMarker(suffix))
            {
                return len;
            }
        }

        return 0;
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

    private static bool IsPrefixOfToolRoutingMarker(string suffix)
    {
        const string target = "to=functions.";
        if (target.StartsWith(suffix, StringComparison.Ordinal)) return true;

        if (suffix.StartsWith(target, StringComparison.Ordinal))
        {
            // Characters after `to=functions.` should be alphanumeric / underscore
            for (int i = target.Length; i < suffix.Length; i++)
            {
                char c = suffix[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }
            return true;
        }

        return false;
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
