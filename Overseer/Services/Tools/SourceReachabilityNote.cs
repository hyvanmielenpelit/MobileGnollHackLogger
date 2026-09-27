using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// Writes a one-line "[Not reachable: …]" note when a rendered <c>get_function_definition</c>,
    /// <c>source_code_view</c> or <c>source_code_search</c> result shows the column-0 definition of a
    /// GnollHack function that <see cref="SourceLivenessIndex"/> finds no live reference to. At most
    /// one note per result, naming the first such function shown. Never throws: any failure, or an
    /// index that is not ready, yields no note.
    /// </summary>
    internal static class SourceReachabilityNote
    {
        public const int MaxLength = 300;

        /// <summary>Distinct definitions checked per view or search result, bounding a cold lookup's cost.</summary>
        private const int MaxNamesChecked = 5;

        /// <summary>Rows from a function definition result's first line searched for the declaring line.</summary>
        private const int DeclaringLineRows = 5;

        private static readonly Regex FunctionHeaderRegex = new(@"^--- (.+?):L(\d+)-L(\d+) \((.+?), \d+ lines?\) ---", RegexOptions.Compiled);
        private static readonly Regex ViewHeaderRegex = new(@"^--- (.+?):L\d+-L\d+ ---\s*$", RegexOptions.Compiled);
        private static readonly Regex ViewLineRegex = new(@"^(\d+): (.*)$", RegexOptions.Compiled);
        private static readonly Regex SearchHeaderRegex = new(@"^--- (.+?):L\d+ ---\s*$", RegexOptions.Compiled);
        private static readonly Regex SearchMarkedLineRegex = new(@"^>>> (\d+): (.*)$", RegexOptions.Compiled);

        /// <summary>The note naming <paramref name="name"/>, or null when it would exceed <see cref="MaxLength"/>.</summary>
        public static string? Build(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string note = $"[Not reachable: {name}() has no live call site in the indexed source, so the game never runs it.]";
            return note.Length <= MaxLength ? note : null;
        }

        /// <summary>
        /// The note for a <c>get_function_definition</c> result whose header names a function: one of
        /// the header range's first rows is that function's column-0 definition.
        /// </summary>
        public static string? ForFunctionDefinitionResult(string? content, SourceLivenessIndex? index)
        {
            try
            {
                if (string.IsNullOrEmpty(content) || index == null) return null;

                string firstLine = content.Split('\n')[0].TrimEnd('\r');
                var header = FunctionHeaderRegex.Match(firstLine);
                if (!header.Success) return null;
                if (content.Contains(" is outside this definition: ", StringComparison.Ordinal)) return null;

                var view = index.TryGetView();
                if (view == null) return null;

                string path = header.Groups[1].Value;
                int start = int.Parse(header.Groups[2].Value);
                int end = int.Parse(header.Groups[3].Value);
                string requested = header.Groups[4].Value;

                for (int line = start; line <= Math.Min(end, start + DeclaringLineRows - 1); line++)
                {
                    string? defined = view.DefinitionAt(path, line);
                    if (defined == null || !defined.Equals(requested, StringComparison.OrdinalIgnoreCase)) continue;
                    return view.IsLive(defined) ? null : Build(defined);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The note for a <c>source_code_view</c> result, from the definitions among its numbered lines.</summary>
        public static string? ForViewResult(string? content, SourceLivenessIndex? index)
        {
            try
            {
                if (string.IsNullOrEmpty(content) || index == null) return null;

                string? path = null;
                var lines = new List<int>();
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
                    if (numbered.Success) lines.Add(int.Parse(numbered.Groups[1].Value));
                }

                if (path == null || lines.Count == 0) return null;

                var view = index.TryGetView();
                if (view == null) return null;

                var candidates = new List<(string Path, int Line)>();
                foreach (int line in lines) candidates.Add((path, line));
                return FirstUnreachable(view, candidates);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The note for a <c>source_code_search</c> result, from the definitions among its "&gt;&gt;&gt;"-marked matches.</summary>
        public static string? ForSearchMatches(string? content, SourceLivenessIndex? index)
        {
            try
            {
                if (string.IsNullOrEmpty(content) || index == null) return null;

                string? path = null;
                var candidates = new List<(string Path, int Line)>();
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
                    if (marked.Success) candidates.Add((path, int.Parse(marked.Groups[1].Value)));
                }

                if (candidates.Count == 0) return null;

                var view = index.TryGetView();
                return view == null ? null : FirstUnreachable(view, candidates);
            }
            catch
            {
                return null;
            }
        }

        private static string? FirstUnreachable(SourceLivenessIndex.CorpusView view, List<(string Path, int Line)> candidates)
        {
            var checkedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (path, line) in candidates)
            {
                string? defined = view.DefinitionAt(path, line);
                if (defined == null || !checkedNames.Add(defined)) continue;
                if (!view.IsLive(defined)) return Build(defined);
                if (checkedNames.Count == MaxNamesChecked) break;
            }

            return null;
        }
    }
}
