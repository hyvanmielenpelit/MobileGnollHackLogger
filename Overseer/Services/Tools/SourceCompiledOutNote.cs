using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// Finds "#if 0 ... #endif" (or its "#else"/"#elif") blocks that a viewed or extracted line
    /// range falls inside, and writes a one-line note saying those lines are compiled out. Scans a
    /// file's full line list from the top so nesting of every "#if", "#ifdef" and "#ifndef" is
    /// tracked correctly; only a bare "#if 0" is ever treated as compiled out, never a guessed
    /// "#ifdef" flag.
    /// </summary>
    internal static class SourceCompiledOutNote
    {
        public const int MaxLength = 300;
        private const int MaxRangesNamed = 3;
        private const int MaxMatchesNamed = 2;

        private static readonly Regex ZeroIfRegex = new(@"^\s*#\s*if\s+0\b", RegexOptions.Compiled);
        private static readonly Regex IfRegex = new(@"^\s*#\s*if\s", RegexOptions.Compiled);
        private static readonly Regex IfdefRegex = new(@"^\s*#\s*ifdef\b", RegexOptions.Compiled);
        private static readonly Regex IfndefRegex = new(@"^\s*#\s*ifndef\b", RegexOptions.Compiled);
        private static readonly Regex ElseRegex = new(@"^\s*#\s*else\b", RegexOptions.Compiled);
        private static readonly Regex ElifRegex = new(@"^\s*#\s*elif\b", RegexOptions.Compiled);
        private static readonly Regex EndifRegex = new(@"^\s*#\s*endif\b", RegexOptions.Compiled);

        private static readonly Regex ViewHeaderRegex = new(@"^--- (.+?):L\d+-L\d+ ---\s*$", RegexOptions.Compiled);
        private static readonly Regex ViewLineRegex = new(@"^(\d+): (.*)$", RegexOptions.Compiled);
        private static readonly Regex FunctionHeaderRegex = new(@"^--- (.+?):L(\d+)-L(\d+) \(.+?\) ---", RegexOptions.Compiled);
        private static readonly Regex FunctionTruncationRegex = new(
            @"\[Output truncated at line \d+ of \d+\. Call again with start_line=\d+ \(file line (\d+)\) to continue\.\]",
            RegexOptions.Compiled);
        private static readonly Regex SearchHeaderRegex = new(@"^--- (.+?):L\d+ ---\s*$", RegexOptions.Compiled);
        private static readonly Regex SearchMarkedLineRegex = new(@"^>>> (\d+): (.*)$", RegexOptions.Compiled);

        /// <summary>
        /// The sub-ranges of <paramref name="startLine"/>-<paramref name="endLine"/> (both 1-based,
        /// inclusive) that lie inside a "#if 0" region, clipped to that range. Scans every line of
        /// the file from the top, so a region opened before <paramref name="startLine"/> is still
        /// recognised.
        /// </summary>
        public static List<(int Start, int End)> FindRanges(IReadOnlyList<string> lines, int startLine, int endLine)
        {
            var ranges = new List<(int Start, int End)>();
            if (lines == null || lines.Count == 0) return ranges;

            startLine = Math.Max(1, startLine);
            endLine = Math.Min(lines.Count, endLine);
            if (startLine > endLine) return ranges;

            var stack = new Stack<bool>();
            int activeZeroCount = 0;
            int? pendingStart = null;

            void CloseRegion(int closingLine)
            {
                if (!pendingStart.HasValue) return;
                int regionStart = pendingStart.Value;
                int regionEnd = closingLine - 1;
                pendingStart = null;
                if (regionEnd < regionStart) return;

                int overlapStart = Math.Max(regionStart, startLine);
                int overlapEnd = Math.Min(regionEnd, endLine);
                if (overlapStart <= overlapEnd) ranges.Add((overlapStart, overlapEnd));
            }

            for (int i = 0; i < lines.Count; i++)
            {
                int lineNumber = i + 1;
                string line = lines[i];

                if (ZeroIfRegex.IsMatch(line))
                {
                    stack.Push(true);
                    activeZeroCount++;
                    if (activeZeroCount == 1) pendingStart = lineNumber + 1;
                }
                else if (IfRegex.IsMatch(line) || IfdefRegex.IsMatch(line) || IfndefRegex.IsMatch(line))
                {
                    stack.Push(false);
                }
                else if (ElseRegex.IsMatch(line) || ElifRegex.IsMatch(line))
                {
                    // "#else"/"#elif" always applies to the innermost open directive; when that
                    // directive is the "#if 0" itself, its branch is now live code, so the zero
                    // region contributed by this frame ends here.
                    if (stack.Count > 0 && stack.Peek())
                    {
                        stack.Pop();
                        stack.Push(false);
                        activeZeroCount--;
                        if (activeZeroCount == 0) CloseRegion(lineNumber);
                    }
                }
                else if (EndifRegex.IsMatch(line))
                {
                    if (stack.Count > 0 && stack.Pop())
                    {
                        activeZeroCount--;
                        if (activeZeroCount == 0) CloseRegion(lineNumber);
                    }
                }
            }

            // An "#if 0" with no matching "#endif" runs to the end of the file.
            if (pendingStart.HasValue)
            {
                int overlapStart = Math.Max(pendingStart.Value, startLine);
                int overlapEnd = Math.Min(lines.Count, endLine);
                if (overlapStart <= overlapEnd) ranges.Add((overlapStart, overlapEnd));
            }

            return ranges;
        }

        /// <summary>Whether a single file line lies inside a "#if 0" region.</summary>
        public static bool IsInsideCompiledOutRegion(IReadOnlyList<string> lines, int line) =>
            FindRanges(lines, line, line).Count > 0;

        /// <summary>
        /// The "[Not compiled: ...]" note for a range of a file's lines, or null when none of it
        /// lies inside a "#if 0" region. Never throws.
        /// </summary>
        public static string? Build(IReadOnlyList<string>? lines, int startLine, int endLine)
        {
            try
            {
                if (lines == null || lines.Count == 0 || startLine > endLine) return null;
                return Compose(FindRanges(lines, startLine, endLine));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The note for a rendered <c>source_code_view</c> result, using its numbered body lines
        /// (rather than the header's requested range) to find the range actually shown.
        /// </summary>
        public static string? ForViewResult(string? content, Func<string, string[]?> lineLookup)
        {
            try
            {
                if (string.IsNullOrEmpty(content) || lineLookup == null) return null;

                string? path = null;
                int? minLine = null;
                int? maxLine = null;
                foreach (string raw in content.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (path == null)
                    {
                        var header = ViewHeaderRegex.Match(line);
                        if (header.Success) path = header.Groups[1].Value;
                        continue;
                    }

                    var numbered = ViewLineRegex.Match(line);
                    if (!numbered.Success) continue;
                    int n = int.Parse(numbered.Groups[1].Value);
                    minLine = minLine.HasValue ? Math.Min(minLine.Value, n) : n;
                    maxLine = maxLine.HasValue ? Math.Max(maxLine.Value, n) : n;
                }

                if (path == null || !minLine.HasValue) return null;
                return Build(lineLookup(path), minLine.Value, maxLine!.Value);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The note for a rendered <c>get_function_definition</c> result, from its header's file
        /// range, cut down to the truncation notice's last shown line when the body was truncated.
        /// </summary>
        public static string? ForFunctionDefinitionResult(string? content, Func<string, string[]?> lineLookup)
        {
            try
            {
                if (string.IsNullOrEmpty(content) || lineLookup == null) return null;

                string firstLine = content.Split('\n')[0].TrimEnd('\r');
                var header = FunctionHeaderRegex.Match(firstLine);
                if (!header.Success) return null;

                string path = header.Groups[1].Value;
                int start = int.Parse(header.Groups[2].Value);
                int end = int.Parse(header.Groups[3].Value);

                var truncation = FunctionTruncationRegex.Match(content);
                if (truncation.Success) end = int.Parse(truncation.Groups[1].Value) - 1;

                return Build(lineLookup(path), start, end);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The note for a rendered <c>source_code_search</c> result: names up to the first two
        /// ">>>"-marked matches, across however many files the result covers, that fall inside a
        /// "#if 0" region.
        /// </summary>
        public static string? ForSearchMatches(string? content, Func<string, string[]?> lineLookup)
        {
            try
            {
                if (string.IsNullOrEmpty(content) || lineLookup == null) return null;

                string? path = null;
                var compiledOut = new List<(string Path, int Line)>();
                var lineCache = new Dictionary<string, string[]?>(StringComparer.OrdinalIgnoreCase);

                foreach (string raw in content.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    var header = SearchHeaderRegex.Match(line);
                    if (header.Success)
                    {
                        path = header.Groups[1].Value;
                        continue;
                    }

                    if (path == null) continue;
                    var marked = SearchMarkedLineRegex.Match(line);
                    if (!marked.Success) continue;

                    if (!lineCache.TryGetValue(path, out var fileLines))
                    {
                        fileLines = lineLookup(path);
                        lineCache[path] = fileLines;
                    }
                    if (fileLines == null) continue;

                    int matchLine = int.Parse(marked.Groups[1].Value);
                    if (IsInsideCompiledOutRegion(fileLines, matchLine))
                    {
                        compiledOut.Add((path, matchLine));
                        if (compiledOut.Count == MaxMatchesNamed) break;
                    }
                }

                return ComposeMatchNote(compiledOut);
            }
            catch
            {
                return null;
            }
        }

        private static string? Compose(List<(int Start, int End)> ranges)
        {
            if (ranges.Count == 0) return null;

            for (int shown = Math.Min(ranges.Count, MaxRangesNamed); shown >= 1; shown--)
            {
                var tokens = ranges.Take(shown)
                    .Select(r => r.Start == r.End ? r.Start.ToString() : $"{r.Start}-{r.End}")
                    .ToList();
                int remaining = ranges.Count - shown;
                if (remaining > 0) tokens.Add($"{remaining} more");

                bool singleLine = shown == 1 && remaining == 0 && ranges[0].Start == ranges[0].End;
                string subject = singleLine ? "file line" : "file lines";
                string verb = singleLine ? "is" : "are";

                string note = $"[Not compiled: {subject} {JoinNatural(tokens)} {verb} inside #if 0 ... #endif."
                    + " They show removed or disabled code, not what the game does; the live code is outside them.]";
                if (note.Length <= MaxLength) return note;
            }

            return null;
        }

        private static string? ComposeMatchNote(List<(string Path, int Line)> matches)
        {
            if (matches.Count == 0) return null;

            for (int shown = matches.Count; shown >= 1; shown--)
            {
                var subset = matches.Take(shown).Select(m => $"{m.Path}:{m.Line}").ToList();
                string subject = subset.Count == 1 ? "match" : "matches";
                string verb = subset.Count == 1 ? "is" : "are";

                string note = $"[Not compiled: the {subject} at {JoinNatural(subset)} {verb} inside #if 0 ... #endif.]";
                if (note.Length <= MaxLength) return note;
            }

            return null;
        }

        /// <summary>Joins two or three items as "A and B" / "A, B and C"; one item is returned as-is.</summary>
        private static string JoinNatural(List<string> items)
        {
            if (items.Count == 1) return items[0];
            if (items.Count == 2) return $"{items[0]} and {items[1]}";
            return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
        }
    }
}
