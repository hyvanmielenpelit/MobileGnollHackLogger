using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// Finds C function definition lines in a rendered <c>source_code_search</c> or
    /// <c>source_code_view</c> result and writes a one-line pointer to <c>get_function_definition</c>,
    /// which returns a whole body in one call. Line-pattern heuristics, not a C parser: a missed
    /// definition yields no pointer, and a false one leads to that tool's own miss payload.
    /// </summary>
    internal static class SourceDefinitionHint
    {
        public const int MaxLength = 300;
        private const int MaxNames = 2;
        private const int ViewOpeningLines = 3;

        private static readonly Regex SearchHeaderRegex = new(@"^--- (.+?):L\d+ ---\s*$", RegexOptions.Compiled);
        private static readonly Regex SearchMarkedLineRegex = new(@"^>>> (\d+): (.*)$", RegexOptions.Compiled);
        private static readonly Regex ViewHeaderRegex = new(@"^--- (.+?):L\d+-L\d+ ---\s*$", RegexOptions.Compiled);
        private static readonly Regex ViewLineRegex = new(@"^(\d+): (.*)$", RegexOptions.Compiled);

        // The column-0 shapes BenchmarkCitationLivenessCheck recognises: `name(`, or type tokens then `name(`.
        private static readonly Regex BareDefinitionRegex = new(@"^([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled);
        private static readonly Regex TypedDefinitionRegex = new(
            @"^(?!(?:extern|return|else|if|while|for|switch|case|goto|sizeof)\b)(?:[A-Za-z_]\w*\s+|\*\s*)+\**([A-Za-z_]\w*)\s*\((?!.*;\s*$)",
            RegexOptions.Compiled);

        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "if", "while", "for", "switch", "return", "sizeof", "else", "case", "goto", "do", "defined"
        };

        /// <summary>The pointer for the <c>&gt;&gt;&gt; </c>-marked definition lines of a search result, or null.</summary>
        public static string? ForSearchResult(string? content, string repository = "gnollhack")
        {
            if (string.IsNullOrEmpty(content)) return null;

            var found = new List<(string Name, string Path, int Line)>();
            string? path = null;
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

                string? name = DefinitionName(marked.Groups[2].Value);
                if (name == null || !IsCFile(path)) continue;
                if (found.Any(f => f.Name == name)) continue;
                found.Add((name, path, int.Parse(marked.Groups[1].Value)));
                if (found.Count == MaxNames) break;
            }

            return Compose(found, repository);
        }

        /// <summary>
        /// The pointer for a view that opens on a definition (one of its first three lines) and ends
        /// before that definition's column-0 closing brace, or null.
        /// </summary>
        public static string? ForViewResult(string? content, string repository = "gnollhack")
        {
            if (string.IsNullOrEmpty(content)) return null;

            string? path = null;
            var body = new List<(int Line, string Text)>();
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
                if (numbered.Success)
                {
                    body.Add((int.Parse(numbered.Groups[1].Value), numbered.Groups[2].Value));
                }
            }

            if (path == null || !IsCFile(path) || body.Count == 0) return null;

            (string Name, int Line)? definition = null;
            foreach (var (lineNumber, text) in body.Take(ViewOpeningLines))
            {
                string? name = DefinitionName(text);
                if (name != null)
                {
                    definition = (name, lineNumber);
                    break;
                }
            }
            if (definition == null) return null;

            var last = body.LastOrDefault(b => !string.IsNullOrWhiteSpace(b.Text));
            if (last.Text != null && last.Text.StartsWith('}')) return null;

            return Compose(new List<(string, string, int)> { (definition.Value.Name, path, definition.Value.Line) }, repository);
        }

        /// <summary>The defined name when <paramref name="text"/> is a column-0 definition line, else null.</summary>
        internal static string? DefinitionName(string text)
        {
            string trimmed = text.TrimEnd();
            if (!(trimmed.EndsWith(')') || trimmed.EndsWith('{'))) return null;

            var bare = BareDefinitionRegex.Match(text);
            if (bare.Success) return Keywords.Contains(bare.Groups[1].Value) ? null : bare.Groups[1].Value;

            var typed = TypedDefinitionRegex.Match(text);
            if (typed.Success) return Keywords.Contains(typed.Groups[1].Value) ? null : typed.Groups[1].Value;

            return null;
        }

        private static bool IsCFile(string path) => path.EndsWith(".c", StringComparison.OrdinalIgnoreCase);

        private static string? Compose(List<(string Name, string Path, int Line)> found, string repository)
        {
            for (int count = found.Count; count > 0; count--)
            {
                var names = found.Take(count).ToList();
                string locations = string.Join("; ", names.Select(f => $"{f.Name}() at {f.Path}:{f.Line}"));
                string repositoryArgument = string.Equals(repository, "nethack", StringComparison.OrdinalIgnoreCase)
                    ? ", \"repository\": \"nethack\""
                    : string.Empty;
                string hint = $"[Definition: {locations}. get_function_definition {{\"name\": \"{names[0].Name}\"{repositoryArgument}}} returns the whole body in one call; paging it with source_code_view costs one model round per page.]";
                if (hint.Length <= MaxLength) return hint;
            }

            return null;
        }
    }
}
