namespace Overseer.Services.Benchmarking;

using System;
using System.Text;
using System.Text.RegularExpressions;

public record SanitizedAnswer(string AnswerText, string? ThoughtText);

public static class BenchmarkAnswerSanitizer
{
    private static readonly Regex ThoughtDivRegex = new(@"<div\s+class=[""']ai-thought[""']>([\s\S]*?)(?:</div>|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodeBlockRegex = new(@"(```[\s\S]*?```)", RegexOptions.Compiled);

    public static SanitizedAnswer Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SanitizedAnswer(string.Empty, null);
        }

        // 1. Extract thought text
        var thoughtMatches = ThoughtDivRegex.Matches(text);
        string? thoughtText = null;
        if (thoughtMatches.Count > 0)
        {
            var sbThoughts = new StringBuilder();
            foreach (Match match in thoughtMatches)
            {
                var content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                {
                    if (sbThoughts.Length > 0)
                    {
                        sbThoughts.Append("\n\n");
                    }
                    sbThoughts.Append(content);
                }
            }
            if (sbThoughts.Length > 0)
            {
                thoughtText = sbThoughts.ToString();
            }
        }

        // 2. Strip thought divs for answer text
        string stripped = ThoughtDivRegex.Replace(text, string.Empty);

        // 3. Preserve code blocks and collapse extra newlines outside code blocks
        var parts = CodeBlockRegex.Split(stripped);
        var sbAnswer = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) // Outside code blocks
            {
                string part = parts[i].Replace("\r\n", "\n");
                part = Regex.Replace(part, @"\n{3,}", "\n\n");
                sbAnswer.Append(part);
            }
            else // Inside code block
            {
                sbAnswer.Append(parts[i]);
            }
        }

        string answerText = sbAnswer.ToString().Trim();
        return new SanitizedAnswer(answerText, thoughtText);
    }
}
