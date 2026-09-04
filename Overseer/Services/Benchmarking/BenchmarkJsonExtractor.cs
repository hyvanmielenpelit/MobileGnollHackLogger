namespace Overseer.Services.Benchmarking;

using System;
using System.Text.RegularExpressions;

public static class BenchmarkJsonExtractor
{
    private static readonly Regex CodeFenceRegex = new(
        @"```([A-Za-z0-9_+#-]*)[ \t]*\r?\n([\s\S]*?)```",
        RegexOptions.Compiled);

    /// <summary>
    /// Extracts a JSON payload from a model response that may contain Markdown code fences,
    /// surrounding prose, or preceding non-JSON code blocks (such as C source citations).
    /// </summary>
    public static string Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text?.Trim() ?? string.Empty;
        }

        string trimmed = text.Trim();

        // 1 & 2: Enumerate every fenced block; return the first payload whose first non-whitespace character is '{' or '['.
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

        // 3: If no fenced payload qualifies, fall back to the existing outermost '['...']' / '{'...'}'
        // scan over the whole text, preserving array-before-object precedence.
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
}
