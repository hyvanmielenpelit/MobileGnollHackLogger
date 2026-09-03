namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

public sealed record BenchmarkScrubResult(
    string AnswerText,
    string? ArtifactText,
    int ArtifactBlockCount,
    BenchmarkAnswerFlags Flags);

/// <summary>
/// Removes provider transport artifacts from a benchmark answer before it is graded.
///
/// This is a second line of defence behind <see cref="Providers.ReasoningTextSanitizer"/>. The
/// sanitizer runs on every Overseer turn and is deliberately conservative; the benchmark has a
/// stricter requirement, because an assessor that sees leaked tool-call payloads grades them —
/// inconsistently, as the 2026-09-03 GPT-5.6 Luna run showed, where four answers carrying the
/// same defect scored 94-99 and one scored 70.
///
/// Everything removed is returned in <see cref="BenchmarkScrubResult.ArtifactText"/> and stored
/// alongside the answer, so a scrubber that removes too much is visible rather than silent.
/// </summary>
public sealed class BenchmarkArtifactScrubber
{
    /// <summary>
    /// Tool parameter names that authored prose about GnollHack would not plausibly use as JSON
    /// keys. One of these in a bare object is enough to identify a leaked tool payload.
    ///
    /// `function_name` and `recipient_name` are here even though no registered tool declares
    /// them: models hallucinate parameter names, and the 2026-09-03 run leaked
    /// {"function_name":"get_encounter_monster_count","repository":"gnollhack"} — a payload no
    /// schema-derived vocabulary would have matched.
    /// </summary>
    public static readonly IReadOnlySet<string> DistinctiveParameterNames = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "repository", "file_filter", "filenames_only", "context_lines", "search_term",
        "path_filter", "prefix_filter", "namespace_filter", "is_regex", "whole_word",
        "max_results", "line_count", "start_line", "tool_uses", "function_name",
        "recipient_name"
    };

    /// <summary>
    /// Remaining parameter names of the tools the benchmark exposes. These are ordinary English
    /// words, so one alone proves nothing; two or more with no foreign key does.
    /// </summary>
    public static readonly IReadOnlySet<string> GenericParameterNames = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "query", "name", "type", "file", "article", "section", "category", "topic",
        "symbol", "kind", "parameters"
    };

    // A payload must clear this before the answer tail is trusted as the real answer. Below it,
    // only the payload spans themselves are removed: a trailing payload must never be allowed to
    // truncate an entire authored answer.
    private const int MinAnswerTailChars = 200;

    private static readonly Regex MarkdownBlockRegex =
        new(@"^\s{0,3}(?:#{1,6}\s|[-*+]\s|\d+\.\s|>\s|\||```)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ResidualRoutingMarkerRegex =
        new(@"to=[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+", RegexOptions.Compiled);

    private static readonly Regex ControlTokenRegex =
        new(@"<\|[a-zA-Z_]{1,32}\|>", RegexOptions.Compiled);

    // Investigation narration: the model announcing tool work it is about to do, or has just
    // done, in the first person. This is analysis-channel content, not an answer to the question.
    private static readonly Regex NarrationSignatureRegex = new(
        @"\b(?:I(?:’|')m|I am|I(?:’|')ll|I will|Let me)\s+(?:also\s+|now\s+|just\s+)?" +
        @"(?:check|verify|locat|trac|confirm|look|read|search|inspect|examin|resolv|find)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "The code shows/confirms ... I'm ..." — narration that opens with a finding rather than an
    // intention, which is how Q2, Q13 and Q16 begin.
    private static readonly Regex NarrationFindingRegex = new(
        @"^\s*(?:The|This)\s+(?:code|combat code|source|implementation|check|gift check)\b" +
        @"[\s\S]{0,600}?\b(?:I(?:’|')m|I am|I(?:’|')ll|I will)\s+(?:also\s+|now\s+)?" +
        @"(?:check|verify|locat|trac|confirm|look|read|search|inspect|examin)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static BenchmarkArtifactScrubber Default { get; } = new();

    /// <summary>
    /// Whether the text still carries transport artifacts. Used by
    /// <see cref="HarnessArtifactDetector"/>, which previously keyed on three narrow regexes and
    /// therefore missed three of the seven affected answers in the 2026-09-03 run.
    /// </summary>
    public bool HasArtifacts(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (ResidualRoutingMarkerRegex.IsMatch(text)) return true;
        if (ControlTokenRegex.IsMatch(text)) return true;

        return FindPayloadSpans(text).Count > 0;
    }

    public BenchmarkScrubResult Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new BenchmarkScrubResult(string.Empty, null, 0, BenchmarkAnswerFlags.None);
        }

        string working = text.Replace("\r\n", "\n");
        var flags = BenchmarkAnswerFlags.None;
        var removed = new List<string>();

        // 0. Residual routing markers and control tokens. ReasoningTextSanitizer normally takes
        //    these upstream, but the benchmark must not depend on that having succeeded, and a
        //    marker whose payload sits mid-line is not found by the payload rule below.
        working = StripResidualMarkers(working, removed);
        if (removed.Count > 0)
        {
            flags |= BenchmarkAnswerFlags.HarnessArtifacts;
        }

        var spans = FindPayloadSpans(working);
        string answer = working;

        if (spans.Count > 0)
        {
            flags |= BenchmarkAnswerFlags.HarnessArtifacts;

            int lastEnd = spans[^1].End;
            string tail = working.Substring(lastEnd);

            if (IsSubstantialAnswerTail(tail))
            {
                // Everything up to the final leaked payload is analysis-channel content:
                // authored answer text cannot precede a tool-call attempt. This is the
                // structural half of the narration rule and needs no heuristic.
                string prefix = working.Substring(0, lastEnd);
                removed.Add(prefix);
                answer = tail;

                if (ContainsNarrationProse(prefix, spans))
                {
                    flags |= BenchmarkAnswerFlags.ReasoningBleed;
                }
            }
            else
            {
                // A payload at or near the end. Remove only the payload spans and the junk
                // immediately adjacent to them, and keep the surrounding prose.
                var sb = new StringBuilder();
                int cursor = 0;
                foreach (var span in spans)
                {
                    var (start, end) = ExpandSpanOverAdjacentJunk(working, span);
                    if (start > cursor)
                    {
                        sb.Append(working, cursor, start - cursor);
                    }
                    removed.Add(working.Substring(start, end - start));
                    cursor = end;
                }
                if (cursor < working.Length)
                {
                    sb.Append(working, cursor, working.Length - cursor);
                }
                answer = sb.ToString();
            }
        }

        // Narration prefix with no leaked payload at all (Q2, Q11, Q17 of the reference run).
        var (narrationStripped, narration) = StripNarrationPrefix(answer);
        if (narration != null)
        {
            answer = narrationStripped;
            removed.Insert(0, narration);
            flags |= BenchmarkAnswerFlags.ReasoningBleed;
        }

        string? artifactText = removed.Count > 0
            ? string.Join("\n\n---\n\n", removed.Select(r => r.Trim()).Where(r => r.Length > 0))
            : null;

        if (string.IsNullOrWhiteSpace(artifactText))
        {
            artifactText = null;
        }

        // Repetition is only ever looked for inside removed narration, never inside retained
        // answer text, so authored repetition is not touched.
        if (artifactText != null && HasRepeatedFragments(artifactText))
        {
            flags |= BenchmarkAnswerFlags.RepeatedFragments;
        }

        return new BenchmarkScrubResult(answer.Trim(), artifactText, spans.Count, flags);
    }

    // --- Residual markers and control tokens -------------------------------------------------

    private static readonly string[] ChannelLiterals =
    {
        "(commentary code)", "(commentary)", "(json)", "(code)",
        "commentary code", "commentary", "code:", "code", "/json", "json"
    };

    /// <summary>
    /// Removes routing markers and control tokens, together with the JSON payload that follows a
    /// marker. Each removed region is appended to <paramref name="removed"/> verbatim, so the
    /// audit record is the actual text taken rather than a summary of it.
    /// </summary>
    private static string StripResidualMarkers(string text, List<string> removed)
    {
        var fences = FindFenceRegions(text);
        var cuts = new List<(int Start, int End)>();

        foreach (Match m in ResidualRoutingMarkerRegex.Matches(text))
        {
            if (IsInside(fences, m.Index)) continue;

            int end = m.Index + m.Length;

            // Step over whitespace, control tokens and channel literals to reach the payload.
            int probe = end;
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

            if (probe < text.Length && text[probe] == '{' && !IsInside(fences, probe))
            {
                int objectEnd = FindBalancedObjectEnd(text, probe);
                if (objectEnd != -1)
                {
                    end = objectEnd + 1;
                }
            }

            cuts.Add((m.Index, end));
        }

        foreach (Match m in ControlTokenRegex.Matches(text))
        {
            if (IsInside(fences, m.Index)) continue;
            if (cuts.Any(c => m.Index >= c.Start && m.Index < c.End)) continue;
            cuts.Add((m.Index, m.Index + m.Length));
        }

        if (cuts.Count == 0) return text;

        cuts.Sort((a, b) => a.Start.CompareTo(b.Start));

        var sb = new StringBuilder();
        int cursor = 0;
        foreach (var (start, end) in cuts)
        {
            if (start < cursor) continue; // overlapping cut already covered
            if (start > cursor) sb.Append(text, cursor, start - cursor);
            removed.Add(text.Substring(start, end - start));
            cursor = end;
        }
        if (cursor < text.Length) sb.Append(text, cursor, text.Length - cursor);

        return sb.ToString();
    }

    private static int MatchChannelLiteralAt(string text, int index)
    {
        foreach (var literal in ChannelLiterals)
        {
            if (index + literal.Length > text.Length) continue;
            if (!string.Equals(text.Substring(index, literal.Length), literal, StringComparison.Ordinal)) continue;

            int after = index + literal.Length;
            if (after == text.Length || char.IsWhiteSpace(text[after]) || text[after] == '<' || text[after] == '{')
            {
                return literal.Length;
            }
        }

        return 0;
    }

    // --- Payload detection -------------------------------------------------------------------

    private readonly record struct PayloadSpan(int Start, int End);

    /// <summary>
    /// Finds leaked tool-argument payloads: a balanced JSON object that begins a line, sits
    /// outside every fenced code block, and is shaped like a tool argument list.
    ///
    /// Both conditions matter. Position alone would delete an authored JSON object a writer put
    /// on its own line; shape alone would delete a fenced example, which is precisely where a
    /// model legitimately shows raw tool JSON to a reader.
    /// </summary>
    private static List<PayloadSpan> FindPayloadSpans(string text)
    {
        var spans = new List<PayloadSpan>();
        var fences = FindFenceRegions(text);

        int i = 0;
        bool atLineStart = true;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\n')
            {
                atLineStart = true;
                i++;
                continue;
            }

            if (atLineStart && (c == ' ' || c == '\t' || c == '\r'))
            {
                i++;
                continue;
            }

            if (atLineStart && c == '{' && !IsInside(fences, i))
            {
                int end = FindBalancedObjectEnd(text, i);
                if (end != -1 && IsToolArgumentShaped(text.Substring(i, end - i + 1)))
                {
                    spans.Add(new PayloadSpan(i, end + 1));
                    i = end + 1;
                    atLineStart = false;
                    continue;
                }
            }

            atLineStart = false;
            i++;
        }

        return spans;
    }

    /// <summary>
    /// Whether a balanced JSON object looks like a tool argument list: it carries a distinctive
    /// parameter name, or it is made up entirely of two or more known parameter names.
    /// </summary>
    private static bool IsToolArgumentShaped(string json)
    {
        var keys = TopLevelKeys(json);
        if (keys.Count == 0) return false;

        if (keys.Any(DistinctiveParameterNames.Contains))
        {
            return true;
        }

        return keys.Count >= 2 &&
               keys.All(k => GenericParameterNames.Contains(k) || DistinctiveParameterNames.Contains(k));
    }

    /// <summary>
    /// Top-level object keys, found by depth tracking rather than by a JSON parser: a leaked
    /// payload is frequently malformed, and a parser would reject it and let it through.
    /// </summary>
    private static List<string> TopLevelKeys(string json)
    {
        var keys = new List<string>();
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        int stringStart = -1;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"')
                {
                    inString = false;
                    // A string at depth 1 followed by ':' is a top-level key.
                    if (depth == 1)
                    {
                        int j = i + 1;
                        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                        if (j < json.Length && json[j] == ':')
                        {
                            keys.Add(json.Substring(stringStart + 1, i - stringStart - 1));
                        }
                    }
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    stringStart = i;
                    break;
                case '{':
                case '[':
                    depth++;
                    break;
                case '}':
                case ']':
                    depth--;
                    break;
            }
        }

        return keys;
    }

    private static int FindBalancedObjectEnd(string text, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            // Guard against runaway scans over a document that merely opens a brace.
            if (i - start >= 8192) return -1;

            char c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
                if (depth < 0) return -1;
            }
        }

        return -1;
    }

    // --- Adjacent junk -----------------------------------------------------------------------

    // A junk token is a routing marker, a channel literal, or a run of characters carrying no
    // letters at all.
    //
    // Deliberately NOT a catch-all `\S{1,20}`: that would have consumed ordinary prose sitting
    // beside a payload, so an authored sentence like
    // "The repository setting is {"repository":"gnollhack"}" would have lost its opening clause.
    // The structural narration rule already removes everything before the final payload, so the
    // only case this widening serves is a payload trailing the answer — where over-reach is
    // exactly what must not happen.
    private const string JunkToken =
        @"(?:to=[A-Za-z_][A-Za-z0-9_.]*|\(?\s*(?:commentary\s+code|commentary|code:?|/?json)\s*\)?|[^\sA-Za-z]{1,8})";

    private static readonly Regex TrailingJunkRegex =
        new(@"^[ \t]*(?:" + JunkToken + @"[ \t]*)*$", RegexOptions.Compiled);

    /// <summary>
    /// Widens a payload span over the channel literals and garbage tokens that sit on its own
    /// line. The reference run produced runs such as
    /// <c>{...} unerquicklich  code:</c> and <c>   rsat</c> around its payloads.
    /// Only text on the payload's own line is considered, so authored prose on neighbouring
    /// lines is never taken.
    /// </summary>
    private static (int Start, int End) ExpandSpanOverAdjacentJunk(string text, PayloadSpan span)
    {
        // A payload is only ever detected when its brace is the first non-whitespace character
        // of its line, so everything before it on that line is indentation and can always be
        // taken with it. There is deliberately no leading-junk widening beyond that: text with
        // content before the brace is not a payload in the first place.
        int start = text.LastIndexOf('\n', Math.Max(0, span.Start - 1)) + 1;

        int lineEnd = text.IndexOf('\n', span.End);
        if (lineEnd < 0) lineEnd = text.Length;
        string after = text.Substring(span.End, lineEnd - span.End);
        int end = span.End;
        if (after.Trim().Length == 0 || TrailingJunkRegex.IsMatch(after))
        {
            end = lineEnd;
        }

        return (start, end);
    }

    // --- Narration ---------------------------------------------------------------------------

    /// <summary>
    /// Whether the answer text following the last leaked payload is substantial enough to be the
    /// real answer. Guards the structural narration rule: a payload that trails at the end of an
    /// answer must not cause the whole answer to be discarded as narration.
    /// </summary>
    private static bool IsSubstantialAnswerTail(string tail)
    {
        string trimmed = tail.Trim();
        if (trimmed.Length == 0) return false;

        return trimmed.Length >= MinAnswerTailChars || MarkdownBlockRegex.IsMatch(trimmed);
    }

    /// <summary>
    /// Whether a removed prefix contains narration prose, as opposed to being nothing but
    /// payloads and punctuation. Only decides a flag, never what is removed.
    /// </summary>
    private static bool ContainsNarrationProse(string prefix, List<PayloadSpan> spans)
    {
        // Strip the payloads themselves, then see whether prose survives.
        var sb = new StringBuilder();
        int cursor = 0;
        foreach (var span in spans)
        {
            if (span.Start >= prefix.Length) break;
            if (span.Start > cursor) sb.Append(prefix, cursor, span.Start - cursor);
            cursor = Math.Min(span.End, prefix.Length);
        }
        if (cursor < prefix.Length) sb.Append(prefix, cursor, prefix.Length - cursor);

        string prose = ResidualRoutingMarkerRegex.Replace(sb.ToString(), string.Empty);
        int letters = prose.Count(char.IsLetter);
        return letters >= 40;
    }

    /// <summary>
    /// Moves a leading investigation-narration paragraph out of the answer.
    ///
    /// Deliberately conservative: the paragraph must be followed by a blank line and further
    /// content, must carry no Markdown block structure, and must match a narration signature.
    /// All three have to hold, because this is the one rule with no structural proof behind it.
    /// </summary>
    private static (string Answer, string? Narration) StripNarrationPrefix(string text)
    {
        string working = text.TrimStart('\n', '\r', ' ', '\t');
        int split = working.IndexOf("\n\n", StringComparison.Ordinal);
        if (split <= 0) return (text, null);

        string first = working.Substring(0, split);
        string rest = working.Substring(split + 2);

        if (rest.Trim().Length == 0) return (text, null);
        if (MarkdownBlockRegex.IsMatch(first)) return (text, null);

        if (!NarrationSignatureRegex.IsMatch(first) && !NarrationFindingRegex.IsMatch(first))
        {
            return (text, null);
        }

        return (rest, first);
    }

    // --- Repeated fragments ------------------------------------------------------------------

    // Splits on sentence-ending punctuation followed by whitespace, and also on punctuation
    // butted straight against the next sentence's opening character. The second case is the
    // signature of this defect: bled narration arrives with no separator at all, as in
    // "...affects the roll.I’m also locating the shared routine...".
    private static readonly Regex SentenceSplitRegex =
        new(@"(?<=[.!?])(?:\s+|(?=[A-Z“""']))", RegexOptions.Compiled);

    /// <summary>
    /// Whether any sentence occurs three or more times in near-identical form.
    ///
    /// Not "three consecutive": Q7 of the reference run emitted the same sentence four times
    /// with one unrelated sentence interleaved, so a consecutive-run rule never fired on the
    /// very data it was written for.
    /// </summary>
    private static bool HasRepeatedFragments(string text)
    {
        var sentences = SentenceSplitRegex.Split(text)
            .Select(Normalize)
            .Where(s => s.Length >= 20)
            .ToList();

        var counted = new bool[sentences.Count];
        for (int i = 0; i < sentences.Count; i++)
        {
            if (counted[i]) continue;

            int group = 1;
            for (int j = i + 1; j < sentences.Count; j++)
            {
                if (counted[j]) continue;
                if (Similarity(sentences[i], sentences[j]) >= 0.9)
                {
                    counted[j] = true;
                    group++;
                }
            }

            if (group >= 3) return true;
        }

        return false;
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1.0;
        int max = Math.Max(a.Length, b.Length);
        if (max == 0) return 1.0;

        // Length alone can rule a pair out before doing the expensive part.
        if (Math.Abs(a.Length - b.Length) > max * 0.2) return 0.0;

        return 1.0 - (double)Levenshtein(a, b) / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    // --- Fences ------------------------------------------------------------------------------

    private static List<(int Start, int End)> FindFenceRegions(string text)
    {
        var regions = new List<(int, int)>();
        int? openAt = null;
        bool atLineStart = true;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n')
            {
                atLineStart = true;
                continue;
            }

            if (atLineStart && (c == ' ' || c == '\t' || c == '\r')) continue;

            if (atLineStart && c == '`')
            {
                int run = 0;
                while (i + run < text.Length && text[i + run] == '`') run++;
                if (run >= 3)
                {
                    if (openAt == null)
                    {
                        openAt = i;
                    }
                    else
                    {
                        regions.Add((openAt.Value, i + run));
                        openAt = null;
                    }
                    i += run - 1;
                    atLineStart = false;
                    continue;
                }
            }

            atLineStart = false;
        }

        // An unterminated fence protects everything to the end of the text: whatever the model
        // opened, it did not present it as answer prose.
        if (openAt != null)
        {
            regions.Add((openAt.Value, text.Length));
        }

        return regions;
    }

    private static bool IsInside(List<(int Start, int End)> regions, int index)
    {
        foreach (var (start, end) in regions)
        {
            if (index >= start && index < end) return true;
        }
        return false;
    }
}
