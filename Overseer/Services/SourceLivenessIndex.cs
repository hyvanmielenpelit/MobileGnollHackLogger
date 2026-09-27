namespace Overseer.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Overseer.Services.Tools;

/// <summary>
/// Whether a GnollHack function is referenced anywhere in the indexed source or the port sources
/// under <c>win/</c> and <c>sys/</c> (window-port tables and platform mains): a <c>name(</c> call,
/// or a value reference to <c>name</c> (a function pointer passed or stored), outside comments,
/// string literals and <c>#if 0</c> regions, and outside its own definition, a prototype and a
/// <c>#define</c> of it. A function reached only through a macro that builds its name has no
/// reference either.
///
/// Built over the C sources and headers of the corpus it is given, rebuilt when that corpus changes
/// (for the GnollHack repository, when the root or its HEAD SHA changes), and each name's answer is
/// memoized per build. The first build is logged once.
/// </summary>
public sealed class SourceLivenessIndex
{
    private static readonly ConditionalWeakTable<SourceCodeService, SourceLivenessIndex> Shared = new();

    private readonly Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> _corpus;
    private readonly object _gate = new();
    private ILogger? _logger;
    private CorpusView? _view;
    private bool _firstBuildLogged;

    /// <param name="corpus">
    /// Repository-relative, forward-slashed path to the file's lines. Called on every lookup; a new
    /// dictionary instance means a new corpus. It may throw while the corpus is unavailable.
    /// </param>
    /// <param name="logger">Receives the build log line; optional.</param>
    public SourceLivenessIndex(Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> corpus, ILogger? logger = null)
    {
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
        _logger = logger;
    }

    /// <summary>
    /// The one index over the GnollHack repository <paramref name="sourceCode"/> indexes, read from
    /// disk, shared by every caller that passes the same service.
    /// </summary>
    public static SourceLivenessIndex ForSourceCodeService(SourceCodeService sourceCode, int maxFileSizeKB, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        var index = Shared.GetValue(sourceCode, s => new SourceLivenessIndex(new SourceCodeServiceCorpus(s, maxFileSizeKB).Load, logger));
        if (logger != null && index._logger == null) index._logger = logger;
        return index;
    }

    /// <summary>As above, with the per-file size limit read from <c>MaxSourceFileSizeKB</c> (default 800).</summary>
    public static SourceLivenessIndex ForSourceCodeService(SourceCodeService sourceCode, IConfiguration configuration, ILogger? logger = null)
        => ForSourceCodeService(sourceCode, int.TryParse(configuration["MaxSourceFileSizeKB"], out int kb) ? kb : 800, logger);

    /// <summary>
    /// Whether <paramref name="name"/> has a live reference; null when the corpus is unavailable or
    /// anything fails.
    /// </summary>
    public bool? IsLive(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            return GetView().IsLive(name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The current view, or null when the corpus is unavailable.</summary>
    internal CorpusView? TryGetView()
    {
        try
        {
            return GetView();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The current view; throws when the corpus is unavailable.</summary>
    internal CorpusView GetView()
    {
        var corpus = _corpus();
        var view = Volatile.Read(ref _view);
        if (view != null && ReferenceEquals(view.Source, corpus)) return view;

        lock (_gate)
        {
            view = _view;
            if (view != null && ReferenceEquals(view.Source, corpus)) return view;

            var stopwatch = Stopwatch.StartNew();
            var stripped = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var references = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, lines) in corpus)
            {
                string key = path.Replace('\\', '/');
                if (IsPortPath(key))
                {
                    if (HasExtension(key, ReferenceExtensions)) references[key] = StripCommentsAndLiterals(lines);
                }
                else if (HasExtension(key, SourceExtensions))
                {
                    stripped[key] = StripCommentsAndLiterals(lines);
                }
            }

            view = new CorpusView(corpus, stripped, references);
            Volatile.Write(ref _view, view);

            if (!_firstBuildLogged)
            {
                _firstBuildLogged = true;
                _logger?.LogInformation("Source liveness index built over {FileCount} C files in {ElapsedMs} ms.", stripped.Count, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger?.LogDebug("Source liveness index rebuilt over {FileCount} C files in {ElapsedMs} ms.", stripped.Count, stopwatch.ElapsedMilliseconds);
            }

            return view;
        }
    }

    /// <summary>One build: the stripped files, their <c>#if 0</c> masks and the memoized answers.</summary>
    internal sealed class CorpusView
    {
        private readonly ConcurrentDictionary<string, bool> _live = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool[]> _compiledOut = new(StringComparer.OrdinalIgnoreCase);

        public CorpusView(object source, IReadOnlyDictionary<string, string[]> stripped, IReadOnlyDictionary<string, string[]>? references = null)
        {
            Source = source;
            Stripped = stripped;
            References = references ?? new Dictionary<string, string[]>();
        }

        public object Source { get; }

        /// <summary>Path to the file's lines with comments and literals blanked.</summary>
        public IReadOnlyDictionary<string, string[]> Stripped { get; }

        /// <summary>
        /// Port files (<c>win/</c> and <c>sys/</c> outside <c>win/win32/xpl</c>), stripped the same
        /// way. They count as references only: the source tools never show them and a citation
        /// into one is not looked up here.
        /// </summary>
        public IReadOnlyDictionary<string, string[]> References { get; }

        /// <summary>Whether <paramref name="name"/> has a live reference, memoized for this build.</summary>
        public bool IsLive(string name) => _live.GetOrAdd(name, n => HasLiveCallSite(this, n));

        /// <summary>The function whose column-0 definition is on <paramref name="line"/> (1-based) of <paramref name="path"/>, or null.</summary>
        public string? DefinitionAt(string path, int line)
        {
            if (!Stripped.TryGetValue(path.Replace('\\', '/'), out var lines)) return null;
            if (line < 1 || line > lines.Length) return null;
            return DefinitionNameAt(lines, line - 1, lines[line - 1]);
        }

        /// <summary>Whether row <paramref name="row"/> (0-based) of <paramref name="path"/> lies inside a <c>#if 0</c> region.</summary>
        public bool IsCompiledOut(string path, string[] lines, int row)
            => _compiledOut.GetOrAdd(path, _ => CompiledOutMask(lines))[row];

        private static bool[] CompiledOutMask(string[] lines)
        {
            var mask = new bool[lines.Length];
            foreach (var (start, end) in SourceCompiledOutNote.FindRanges(lines, 1, lines.Length))
            {
                for (int n = start; n <= end; n++) mask[n - 1] = true;
            }
            return mask;
        }
    }

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

    /// <summary>
    /// The function whose body holds <paramref name="line"/> (1-based): the nearest column-0
    /// definition line at or above it, provided no column-0 closing brace lies between.
    /// </summary>
    internal static string? EnclosingFunction(CorpusView view, string path, int line)
    {
        if (!view.Stripped.TryGetValue(path, out var lines)) return null;
        if (line < 1 || line > lines.Length) return null;

        for (int i = line - 1; i >= 0; i--)
        {
            string current = lines[i];
            if (i < line - 1 && current.StartsWith('}')) return null;
            if (current.TrimEnd().EndsWith(';')) continue;

            string? name = DefinitionNameAt(lines, i, current);
            if (name != null) return name;
        }

        return null;
    }

    /// <summary>
    /// <paramref name="current"/> (line <paramref name="row"/>, 0-based) as a column-0 function
    /// definition: its name, when <see cref="BareDefinitionRegex"/> or <see cref="TypedDefinitionRegex"/>
    /// matches, the name is not a <see cref="Keywords"/> entry, and <see cref="OpensABody"/> holds for
    /// it; null otherwise.
    /// </summary>
    internal static string? DefinitionNameAt(string[] lines, int row, string current)
    {
        var bare = BareDefinitionRegex.Match(current);
        if (bare.Success && !Keywords.Contains(bare.Groups[1].Value) && OpensABody(lines, row, bare.Groups[1].Index + bare.Groups[1].Length))
        {
            return bare.Groups[1].Value;
        }

        var typed = TypedDefinitionRegex.Match(current);
        if (typed.Success && !Keywords.Contains(typed.Groups[1].Value) && OpensABody(lines, row, typed.Groups[1].Index + typed.Groups[1].Length))
        {
            return typed.Groups[1].Value;
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
    /// Whether <paramref name="name"/> is referenced outside comments, string literals and
    /// <c>#if 0</c> regions: as a call, <c>name(</c>, on a line that is neither a definition of it,
    /// a prototype of it, nor a <c>#define</c> of it; or as a value, the whole token not followed by
    /// <c>(</c> (a function pointer passed or stored), on a line that is not a <c>#define</c> of it.
    /// </summary>
    internal static bool HasLiveCallSite(CorpusView view, string name)
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

        foreach (var (path, file) in view.Stripped.Concat(view.References))
        {
            for (int row = 0; row < file.Length; row++)
            {
                string line = file[row];
                if (!line.Contains(name, StringComparison.Ordinal)) continue;
                if (valueReference.IsMatch(line) && !define.IsMatch(line))
                {
                    if (view.IsCompiledOut(path, file, row)) continue;
                    return true;
                }
                if (!call.IsMatch(line)) continue;
                if (define.IsMatch(line)) continue;
                if (bareDefinition.IsMatch(line) && !line.TrimEnd().EndsWith(';')) continue;
                if (typedDefinition.IsMatch(line)) continue;
                if (line.TrimEnd().EndsWith(';') && prototype.IsMatch(line)) continue;
                if (view.IsCompiledOut(path, file, row)) continue;
                return true;
            }
        }

        return false;
    }

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

    private static readonly string[] TargetDirectories = { "src", "include", "win/win32/xpl" };
    private static readonly string[] PortDirectories = { "win", "sys" };
    private static readonly string[] SourceExtensions = { ".c", ".h" };
    private static readonly string[] ReferenceExtensions = { ".c", ".h", ".cpp", ".m", ".mm" };
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase) { "vis_tab.c", "vis_tab.h", "date.h" };

    /// <summary>A port file: under <c>win/</c> or <c>sys/</c>, outside <c>win/win32/xpl</c>.</summary>
    private static bool IsPortPath(string path)
        => (path.StartsWith("win/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("sys/", StringComparison.OrdinalIgnoreCase))
           && !path.StartsWith("win/win32/xpl/", StringComparison.OrdinalIgnoreCase);

    private static bool HasExtension(string path, string[] extensions)
        => extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The C sources and headers under <paramref name="root"/> that the source indexer carries (its
    /// target directories), plus the port sources under <c>win/</c> and <c>sys/</c> as references;
    /// without dot-, <c>bin</c> and <c>obj</c> directories, the indexer's excluded files and files
    /// over <paramref name="maxFileBytes"/>.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadCorpus(string root, long maxFileBytes)
    {
        var files = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in TargetDirectories.Concat(PortDirectories))
        {
            string full = Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full)) continue;

            foreach (string file in Directory.GetFiles(full, "*.*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (files.ContainsKey(relative)) continue;
                if (relative.Split('/').Any(s => s.StartsWith(".", StringComparison.Ordinal)
                        || string.Equals(s, "bin", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s, "obj", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var info = new FileInfo(file);
                if (!HasExtension(relative, IsPortPath(relative) ? ReferenceExtensions : SourceExtensions)) continue;
                if (ExcludedFiles.Contains(info.Name)) continue;
                if (info.Length > maxFileBytes) continue;

                files[relative] = File.ReadAllLines(file);
            }
        }

        return files;
    }

    /// <summary>
    /// The GnollHack repository <see cref="SourceCodeService"/> indexes, read from its path under the
    /// indexer's directory, extension and size rules (C sources and headers only), and cached until
    /// the repository's root or HEAD moves. Throws while the service has not finished indexing.
    /// </summary>
    private sealed class SourceCodeServiceCorpus
    {
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

                _cached = LoadCorpus(root, _maxFileBytes);
                _cachedKey = key;
                return _cached;
            }
        }
    }
}
