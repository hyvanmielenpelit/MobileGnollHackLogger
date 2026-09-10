namespace Overseer.Services.Benchmarking;

using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class BenchmarkJsonExtractor
{
    private static readonly Regex CodeFenceRegex = new(
        @"```([A-Za-z0-9_+#-]*)[ \t]*\r?\n([\s\S]*?)```",
        RegexOptions.Compiled);

    private static readonly JsonReaderOptions ScanOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Extracts a JSON payload from a model response that may contain Markdown code fences,
    /// surrounding prose, or preceding non-JSON code blocks (such as C source citations).
    /// Outside a fence, the payload is the first <em>complete</em> JSON object or array in
    /// document order, so a bracketed word in prose does not win over the object that follows
    /// it, and text after the object is not carried along. Only when no candidate is a complete
    /// value is the first-to-last bracket span returned, so a malformed object still reaches the
    /// parser and its error is the one recorded.
    /// </summary>
    public static string Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text?.Trim() ?? string.Empty;
        }

        string trimmed = text.Trim();

        // 1: Enumerate every fenced block; return the first payload whose first non-whitespace character is '{' or '['.
        var matches = CodeFenceRegex.Matches(trimmed);
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count >= 3)
            {
                string payload = match.Groups[2].Value.Trim();
                if (payload.Length > 0 && (payload[0] == '{' || payload[0] == '['))
                {
                    return payload;
                }
            }
        }

        // 2: The first '{' or '[' that opens a complete JSON value.
        string? balanced = FindFirstCompleteValue(trimmed);
        if (balanced != null)
        {
            return balanced;
        }

        // 3: The outermost '['...']' / '{'...'}' span over the whole text, with array-before-object precedence.
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

        // 4: If neither yields anything, return trimmed input unchanged.
        return trimmed;
    }

    private static string? FindFirstCompleteValue(string text)
    {
        byte[]? utf8 = null;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '{' && c != '[')
            {
                continue;
            }

            utf8 ??= Encoding.UTF8.GetBytes(text);
            int byteStart = Encoding.UTF8.GetByteCount(text.AsSpan(0, i));

            try
            {
                var reader = new Utf8JsonReader(utf8.AsSpan(byteStart), ScanOptions);
                if (reader.Read() && reader.TrySkip())
                {
                    int byteLength = (int)reader.BytesConsumed;
                    int charLength = Encoding.UTF8.GetCharCount(utf8, byteStart, byteLength);
                    return text.Substring(i, charLength);
                }
            }
            catch (JsonException)
            {
                // Not a complete value from this position; try the next candidate.
            }
        }

        return null;
    }
}
