namespace Overseer.Services.Benchmarking;

using System;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

public record SanitizedAnswer(
    string AnswerText,
    string? ThoughtText,
    string? ScrubbedArtifactText,
    int ScrubbedArtifactCount,
    BenchmarkAnswerFlags Flags);

public static class HarnessArtifactDetector
{
    /// <summary>
    /// Delegates to <see cref="BenchmarkArtifactScrubber"/>.
    ///
    /// This previously keyed on three narrow regexes: <c>to=functions\.</c>, control tokens, and
    /// a single-line JSON pattern requiring a `repository`, `file_filter` or `function_name` key.
    /// That set missed <c>to=multi_tool_use.parallel</c>, multi-line payloads, and narration
    /// bleed entirely — three of the seven affected answers in the 2026-09-03 run went unflagged.
    /// </summary>
    public static bool HasHarnessArtifacts(string? text)
        => BenchmarkArtifactScrubber.Default.HasArtifacts(text);
}

public static class BenchmarkAnswerSanitizer
{
    private static readonly Regex ThoughtDivRegex = new(@"<div\s+class=[""']ai-thought[""']>([\s\S]*?)(?:</div>|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodeBlockRegex = new(@"(```[\s\S]*?```)", RegexOptions.Compiled);

    private static bool ContainsTruncationMarker(string? value)
    {
        return value != null &&
               (value.Contains("[Response truncated", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("[Answer truncated", StringComparison.OrdinalIgnoreCase));
    }

    public static SanitizedAnswer Sanitize(string? text, BenchmarkArtifactScrubber? scrubber = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SanitizedAnswer(string.Empty, null, null, 0, BenchmarkAnswerFlags.Empty);
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

        // 4. Remove transport artifacts, so what is graded is authored text only. Everything
        //    removed is returned for storage and display rather than discarded.
        var scrubResult = (scrubber ?? BenchmarkArtifactScrubber.Default).Scrub(answerText);
        answerText = scrubResult.AnswerText;

        var flags = scrubResult.Flags;
        if (string.IsNullOrWhiteSpace(answerText))
        {
            flags |= BenchmarkAnswerFlags.Empty;
        }

        // Check both the scrubbed answer and the original: a truncation marker can be removed
        // along with a trailing artifact, and the answer is still truncated either way.
        if (ContainsTruncationMarker(answerText) || ContainsTruncationMarker(text))
        {
            flags |= BenchmarkAnswerFlags.Truncated;
        }

        return new SanitizedAnswer(
            answerText ?? string.Empty,
            thoughtText,
            scrubResult.ArtifactText,
            scrubResult.ArtifactBlockCount,
            flags);
    }
}
