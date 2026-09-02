namespace Overseer.Services.Benchmarking;

using System;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

public record SanitizedAnswer(string AnswerText, string? ThoughtText, BenchmarkAnswerFlags Flags);

public static class HarnessArtifactDetector
{
    private static readonly Regex FunctionsToRegex = new(@"to=functions\.", RegexOptions.Compiled);
    private static readonly Regex ControlTokenRegex = new(@"<\|[a-zA-Z_]{1,32}\|>", RegexOptions.Compiled);
    private static readonly Regex BareToolJsonLineRegex = new(@"^\s*\{.*""(?:repository|file_filter|function_name)"".*\}\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    public static bool HasHarnessArtifacts(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return FunctionsToRegex.IsMatch(text)
            || ControlTokenRegex.IsMatch(text)
            || BareToolJsonLineRegex.IsMatch(text);
    }
}

public static class BenchmarkAnswerSanitizer
{
    private static readonly Regex ThoughtDivRegex = new(@"<div\s+class=[""']ai-thought[""']>([\s\S]*?)(?:</div>|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodeBlockRegex = new(@"(```[\s\S]*?```)", RegexOptions.Compiled);

    public static SanitizedAnswer Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SanitizedAnswer(string.Empty, null, BenchmarkAnswerFlags.Empty);
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

        var flags = BenchmarkAnswerFlags.None;
        if (string.IsNullOrWhiteSpace(answerText))
        {
            flags |= BenchmarkAnswerFlags.Empty;
        }

        if (HarnessArtifactDetector.HasHarnessArtifacts(answerText))
        {
            flags |= BenchmarkAnswerFlags.HarnessArtifacts;
        }

        if ((answerText != null && (answerText.Contains("[Response truncated", StringComparison.OrdinalIgnoreCase) || answerText.Contains("[Answer truncated", StringComparison.OrdinalIgnoreCase))) ||
            (text != null && (text.Contains("[Response truncated", StringComparison.OrdinalIgnoreCase) || text.Contains("[Answer truncated", StringComparison.OrdinalIgnoreCase))))
        {
            flags |= BenchmarkAnswerFlags.Truncated;
        }

        return new SanitizedAnswer(answerText, thoughtText, flags);
    }
}
