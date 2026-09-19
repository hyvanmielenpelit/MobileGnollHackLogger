namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Notes a claim verification whose citation, <c>src/&lt;file&gt;.c:&lt;line&gt;</c>, points into a
/// GnollHack function that nothing references: the function enclosing the cited line — a column-0
/// definition whose parameter list is followed by a body, so a macro-table row such as
/// <c>SCROLL(…),</c> is no function — has no <c>name(</c> call and no value reference to
/// <c>name</c> (a function pointer passed or stored) in the indexed GnollHack source outside its own
/// definition, a prototype, a <c>#define</c> and comments. Such a verification is read as
/// Indeterminate for every flag and count (<see cref="BenchmarkClaimVerification.EffectiveVerdict"/>);
/// its stored verdict is not touched.
///
/// A function reached only through a macro that builds its name also has no reference, so the
/// note never argues the opposite verdict — it only withdraws the cited code as evidence. A citation
/// with any other reference (wiki, board, another file) gets no note, nor does a NetHack citation.
/// Pure over the corpus it is given; any failure yields no note.
/// </summary>
public sealed class BenchmarkCitationLivenessCheck
{
    private readonly Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> _corpus;
    private CorpusView? _view;

    /// <param name="corpus">
    /// The indexed GnollHack source: repository-relative, forward-slashed path to the file's lines.
    /// Called once per check; a new dictionary instance means a new corpus.
    /// </param>
    public BenchmarkCitationLivenessCheck(Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> corpus)
    {
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
    }

    /// <summary>A check over the GnollHack repository <paramref name="sourceCode"/> indexes, read from disk.</summary>
    public static BenchmarkCitationLivenessCheck ForSourceCodeService(SourceCodeService sourceCode, int maxFileSizeKB)
        => new(new SourceCodeServiceCorpus(sourceCode, maxFileSizeKB).Load);

    public static string NoteText(string functionName) => $"cited function {functionName} has no live call site";

    private static readonly Regex SourceReferenceRegex = new(
        @"(?<![\w/.-])src/([\w./-]+?\.c):(\d+)(?:\s*[-–]\s*\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OtherReferenceRegex = new(
        @"\bwiki\s*:|\bboard\s*:|[\w-]+/[\w./-]*\.(?:c|h|txt|des|md|cs)\b|\b[\w-]+\.(?:c|h)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A column-0 function definition line as the source indexer recognises one: <c>name(</c> at the
    /// start of the line, or type tokens and <c>name(</c> on one line; neither ending in <c>;</c>.
    /// </summary>
    private static readonly Regex BareDefinitionRegex = new(@"^([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled);
    private static readonly Regex TypedDefinitionRegex = new(
        @"^(?!(?:extern|return|else|if|while|for|switch|case|goto|sizeof)\b)(?:[A-Za-z_]\w*\s+|\*\s*)+\**([A-Za-z_]\w*)\s*\((?!.*;\s*$)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "while", "for", "switch", "return", "sizeof", "else", "case", "goto", "do", "defined"
    };

    /// <summary>The verifications with <see cref="BenchmarkClaimVerification.CitationNote"/> set where it applies.</summary>
    public List<BenchmarkClaimVerification> Annotate(IReadOnlyList<BenchmarkClaimVerification> verifications)
    {
        var result = new List<BenchmarkClaimVerification>(verifications.Count);
        foreach (var v in verifications)
        {
            string? note = v.Verdict == BenchmarkClaimVerdict.Indeterminate ? null : NoteFor(v.Citation);
            result.Add(note == null ? v : v with { CitationNote = note });
        }
        return result;
    }

    /// <summary>The note for <paramref name="citation"/>, or null when it does not apply or anything fails.</summary>
    public string? NoteFor(string? citation)
    {
        try
        {
            return NoteForCore(citation);
        }
        catch
        {
            return null;
        }
    }

    private string? NoteForCore(string? citation)
    {
        if (string.IsNullOrWhiteSpace(citation)) return null;

        string text = citation.Replace('\\', '/');
        if (text.Contains("nethack", StringComparison.OrdinalIgnoreCase)) return null;

        var references = SourceReferenceRegex.Matches(text).Cast<Match>().ToList();
        if (references.Count == 0) return null;
        if (OtherReferenceRegex.IsMatch(SourceReferenceRegex.Replace(text, " "))) return null;

        var view = View();
        var names = new List<string>();
        foreach (var reference in references)
        {
            string path = "src/" + reference.Groups[1].Value;
            if (!int.TryParse(reference.Groups[2].Value, out int line)) return null;

            string? name = EnclosingFunction(view, path, line);
            if (name == null || HasLiveCallSite(view, name)) return null;
            if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }

        return string.Join("; ", names.Select(NoteText));
    }

    /// <summary>
    /// The function whose body holds <paramref name="line"/> (1-based): the nearest column-0
    /// definition line at or above it, provided no column-0 closing brace lies between.
    /// </summary>
    private static string? EnclosingFunction(CorpusView view, string path, int line)
    {
        if (!view.Stripped.TryGetValue(path, out var lines)) return null;
        if (line < 1 || line > lines.Length) return null;

        for (int i = line - 1; i >= 0; i--)
        {
            string current = lines[i];
            if (i < line - 1 && current.StartsWith('}')) return null;
            if (current.TrimEnd().EndsWith(';')) continue;

            var bare = BareDefinitionRegex.Match(current);
            if (bare.Success && !Keywords.Contains(bare.Groups[1].Value) && OpensABody(lines, i, bare.Groups[1].Index + bare.Groups[1].Length))
            {
                return bare.Groups[1].Value;
            }

            var typed = TypedDefinitionRegex.Match(current);
            if (typed.Success && !Keywords.Contains(typed.Groups[1].Value) && OpensABody(lines, i, typed.Groups[1].Index + typed.Groups[1].Length))
            {
                return typed.Groups[1].Value;
            }
        }

        return null;
    }

    private const int MaxParameterListLines = 40;

    /// <summary>
    /// Whether the parameter list opening at or after <paramref name="column"/> of line
    /// <paramref name="row"/> closes within 40 lines and is followed by <c>{</c>, directly or after
    /// K&amp;R parameter declarations (lines ending in <c>;</c>). A macro invocation in a data table
    /// (<c>SCROLL(…),</c>) and a call statement are no function.
    /// </summary>
    private static bool OpensABody(string[] lines, int row, int column)
    {
        int last = Math.Min(lines.Length - 1, row + MaxParameterListLines);
        int depth = 0;
        bool opened = false;
        for (int r = row; r <= last; r++)
        {
            string text = lines[r];
            for (int c = r == row ? column : 0; c < text.Length; c++)
            {
                char ch = text[c];
                if (ch == '(')
                {
                    depth++;
                    opened = true;
                }
                else if (ch == ')' && opened)
                {
                    depth--;
                    if (depth == 0) return FollowedByBody(lines, r, c + 1, last);
                }
            }
        }

        return false;
    }

    private static bool FollowedByBody(string[] lines, int row, int column, int last)
    {
        string rest = lines[row].Substring(column).Trim();
        if (rest.Length > 0) return rest.StartsWith('{');

        for (int r = row + 1; r <= last; r++)
        {
            string text = lines[r].Trim();
            if (text.Length == 0) continue;
            if (text.StartsWith('{')) return true;
            if (!text.EndsWith(';') || lines[r].StartsWith('}')) return false;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is referenced outside comments and string literals: as a call,
    /// <c>name(</c>, on a line that is neither a definition of it, a prototype of it, nor a
    /// <c>#define</c> of it; or as a value, the whole token not followed by <c>(</c> (a function
    /// pointer passed or stored), on a line that is not a <c>#define</c> of it.
    /// </summary>
    private static bool HasLiveCallSite(CorpusView view, string name)
    {
        string escaped = Regex.Escape(name);
        var call = new Regex($@"(?<![\w.])(?<!->){escaped}\s*\(", RegexOptions.CultureInvariant);
        var bareDefinition = new Regex($@"^{escaped}\s*\(", RegexOptions.CultureInvariant);
        var typedDefinition = new Regex(
            $@"^(?!(?:extern|return|else|if|while|for|switch|case|goto|sizeof)\b)(?:[A-Za-z_]\w*\s+|\*\s*)+\**{escaped}\s*\((?!.*;\s*$)",
            RegexOptions.CultureInvariant);
        var prototype = new Regex(
            $@"^\s*(?!(?:return|else|if|while|for|switch|case|goto|sizeof|do)\b)(?:[A-Za-z_]\w*\s+|\*\s*)+\**{escaped}\s*\(",
            RegexOptions.CultureInvariant);
        var define = new Regex($@"^\s*#\s*define\s+{escaped}\b", RegexOptions.CultureInvariant);
        var valueReference = new Regex($@"(?<![\w.])(?<!->){escaped}(?!\w)(?!\s*\()", RegexOptions.CultureInvariant);

        foreach (var file in view.Stripped.Values)
        {
            foreach (string line in file)
            {
                if (!line.Contains(name, StringComparison.Ordinal)) continue;
                if (valueReference.IsMatch(line) && !define.IsMatch(line)) return true;
                if (!call.IsMatch(line)) continue;
                if (define.IsMatch(line)) continue;
                if (bareDefinition.IsMatch(line) && !line.TrimEnd().EndsWith(';')) continue;
                if (typedDefinition.IsMatch(line)) continue;
                if (line.TrimEnd().EndsWith(';') && prototype.IsMatch(line)) continue;
                return true;
            }
        }

        return false;
    }

    private CorpusView View()
    {
        var corpus = _corpus();
        var view = _view;
        if (view != null && ReferenceEquals(view.Source, corpus)) return view;

        var stripped = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, lines) in corpus)
        {
            string key = path.Replace('\\', '/');
            if (key.EndsWith(".c", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
            {
                stripped[key] = StripCommentsAndLiterals(lines);
            }
        }

        view = new CorpusView(corpus, stripped);
        _view = view;
        return view;
    }

    private sealed record CorpusView(object Source, IReadOnlyDictionary<string, string[]> Stripped);

    /// <summary>
    /// The lines with <c>//</c> and <c>/* */</c> comments and string and character literals blanked
    /// to spaces, so every column and line keeps its position. A block comment carries across lines.
    /// </summary>
    internal static string[] StripCommentsAndLiterals(IReadOnlyList<string> lines)
    {
        var result = new string[lines.Count];
        bool inBlock = false;
        for (int n = 0; n < lines.Count; n++)
        {
            string line = lines[n] ?? string.Empty;
            var sb = new StringBuilder(line.Length);
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (inBlock)
                {
                    if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        inBlock = false;
                        sb.Append("  ");
                        i += 2;
                    }
                    else
                    {
                        sb.Append(' ');
                        i++;
                    }
                    continue;
                }

                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    inBlock = true;
                    sb.Append("  ");
                    i += 2;
                    continue;
                }

                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    sb.Append(' ', line.Length - i);
                    break;
                }

                if (c is '"' or '\'')
                {
                    sb.Append(c);
                    i++;
                    while (i < line.Length && line[i] != c)
                    {
                        if (line[i] == '\\' && i + 1 < line.Length)
                        {
                            sb.Append("  ");
                            i += 2;
                            continue;
                        }
                        sb.Append(' ');
                        i++;
                    }
                    if (i < line.Length)
                    {
                        sb.Append(c);
                        i++;
                    }
                    continue;
                }

                sb.Append(c);
                i++;
            }

            result[n] = sb.ToString();
        }

        return result;
    }

    /// <summary>
    /// The GnollHack repository <see cref="SourceCodeService"/> indexes, read from its path under the
    /// indexer's directory, extension and size rules (C sources and headers only), and cached until
    /// the repository's HEAD moves. Throws while the service has not finished indexing.
    /// </summary>
    private sealed class SourceCodeServiceCorpus
    {
        private static readonly string[] TargetDirectories = { "src", "include", "win/win32/xpl" };
        private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase) { "vis_tab.c", "vis_tab.h", "date.h" };

        private readonly SourceCodeService _sourceCode;
        private readonly long _maxFileBytes;
        private readonly object _gate = new();
        private string? _cachedKey;
        private IReadOnlyDictionary<string, IReadOnlyList<string>>? _cached;

        public SourceCodeServiceCorpus(SourceCodeService sourceCode, int maxFileSizeKB)
        {
            _sourceCode = sourceCode;
            _maxFileBytes = (long)maxFileSizeKB * 1024;
        }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Load()
        {
            if (!_sourceCode.IsIndexingComplete)
            {
                throw new InvalidOperationException("The GnollHack source index is not ready.");
            }

            string root = _sourceCode.SourceCodePath;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("The GnollHack source repository is not reachable.");
            }

            string key = root + "|" + (GitHelper.GetGitHeadSha(root) ?? string.Empty);
            lock (_gate)
            {
                if (_cached != null && string.Equals(_cachedKey, key, StringComparison.Ordinal))
                {
                    return _cached;
                }

                var files = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (string dir in TargetDirectories)
                {
                    string full = Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(full)) continue;

                    foreach (string file in Directory.GetFiles(full, "*.*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                        if (relative.Split('/').Any(s => s.StartsWith(".", StringComparison.Ordinal)
                                || string.Equals(s, "bin", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(s, "obj", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var info = new FileInfo(file);
                        string ext = info.Extension.ToLowerInvariant();
                        if (ext != ".c" && ext != ".h") continue;
                        if (ExcludedFiles.Contains(info.Name)) continue;
                        if (info.Length > _maxFileBytes) continue;

                        files[relative] = File.ReadAllLines(file);
                    }
                }

                _cached = files;
                _cachedKey = key;
                return files;
            }
        }
    }
}
