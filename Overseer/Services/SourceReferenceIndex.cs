namespace Overseer.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// The GnollHack functions that reference an identifier: whole-word occurrences in the
/// <c>src/*.c</c> files, outside comments, string and character literals and <c>#if 0</c> regions,
/// each mapped to its enclosing column-0 function definition. Reads the build of
/// <see cref="SourceLivenessIndex"/> it wraps, so it is built, rebuilt and shared exactly as that
/// index is; each identifier's answer is memoized per build.
///
/// Also derives the <c>ART_</c>, <c>PM_</c> and object constants a stats result names, as
/// <c>util/makedefs.c</c> writes them into <c>include/onames.h</c> and <c>include/pm.h</c>.
/// </summary>
public sealed class SourceReferenceIndex
{
    public const int DefaultMax = 12;

    public const string OnamesHeaderPath = "include/onames.h";
    public const string PmHeaderPath = "include/pm.h";

    private static readonly ConditionalWeakTable<SourceCodeService, SourceReferenceIndex> Shared = new();
    private static readonly ConditionalWeakTable<string[], HashSet<string>> HeaderDefines = new();
    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DefineRegex = new(@"^\s*#\s*define\s+([A-Za-z_]\w*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SourceLivenessIndex _liveness;
    private readonly object _gate = new();
    private SourceLivenessIndex.CorpusView? _memoView;
    private ConcurrentDictionary<string, IReadOnlyList<string>> _memo = new(StringComparer.Ordinal);

    /// <param name="corpus">As for the <see cref="SourceLivenessIndex"/> constructor.</param>
    /// <param name="logger">Receives the build log line; optional.</param>
    public SourceReferenceIndex(Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> corpus, ILogger? logger = null)
        : this(new SourceLivenessIndex(corpus, logger))
    {
    }

    internal SourceReferenceIndex(SourceLivenessIndex liveness)
    {
        _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
    }

    /// <summary>
    /// The one index over the GnollHack repository <paramref name="sourceCode"/> indexes, over the
    /// build of the shared <see cref="SourceLivenessIndex"/> of that service.
    /// </summary>
    public static SourceReferenceIndex ForSourceCodeService(SourceCodeService sourceCode, int maxFileSizeKB, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        return Shared.GetValue(sourceCode, s => new SourceReferenceIndex(SourceLivenessIndex.ForSourceCodeService(s, maxFileSizeKB, logger)));
    }

    /// <summary>As above, with the per-file size limit read from <c>MaxSourceFileSizeKB</c> (default 800).</summary>
    public static SourceReferenceIndex ForSourceCodeService(SourceCodeService sourceCode, IConfiguration configuration, ILogger? logger = null)
        => ForSourceCodeService(sourceCode, int.TryParse(configuration["MaxSourceFileSizeKB"], out int kb) ? kb : 800, logger);

    /// <summary>
    /// The distinct functions referencing <paramref name="identifier"/>, in file then line order,
    /// each rendered <c>"&lt;function&gt; (src/&lt;file&gt;.c:&lt;line&gt;)"</c> with the line of its first
    /// reference; past <paramref name="max"/>, one final <c>"… and N more"</c>. Null when the corpus
    /// is unavailable, the identifier is not one, or anything fails.
    /// </summary>
    public IReadOnlyList<string>? FindReferencingFunctions(string identifier, int max = DefaultMax)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierRegex.IsMatch(identifier) || max < 1) return null;
        try
        {
            var view = _liveness.GetView();
            var memo = MemoFor(view);
            return memo.GetOrAdd(identifier + "|" + max.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => Compute(view, identifier, max));
        }
        catch
        {
            return null;
        }
    }

    private ConcurrentDictionary<string, IReadOnlyList<string>> MemoFor(SourceLivenessIndex.CorpusView view)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_memoView, view))
            {
                _memoView = view;
                _memo = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            }
            return _memo;
        }
    }

    private static IReadOnlyList<string> Compute(SourceLivenessIndex.CorpusView view, string identifier, int max)
    {
        var wholeWord = new Regex($@"(?<!\w){Regex.Escape(identifier)}(?!\w)", RegexOptions.CultureInvariant);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var functions = new List<string>();

        foreach (var (path, lines) in view.Stripped
            .Where(f => IsSourceFile(f.Key))
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            for (int row = 0; row < lines.Length; row++)
            {
                string line = lines[row];
                if (!line.Contains(identifier, StringComparison.Ordinal)) continue;
                if (!wholeWord.IsMatch(line)) continue;
                if (view.IsCompiledOut(path, lines, row)) continue;

                string? function = SourceLivenessIndex.EnclosingFunction(view, path, row + 1);
                if (function == null || !seen.Add(path + "|" + function)) continue;
                functions.Add($"{function} ({path}:{row + 1})");
            }
        }

        if (functions.Count <= max) return functions;

        var capped = functions.Take(max).ToList();
        capped.Add($"… and {functions.Count - max} more");
        return capped;
    }

    /// <summary>A GnollHack C source directly under <c>src/</c>.</summary>
    private static bool IsSourceFile(string path)
        => path.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
           && path.EndsWith(".c", StringComparison.OrdinalIgnoreCase)
           && path.IndexOf('/', 4) < 0;

    /// <summary>
    /// The <c>referenced_by</c> list of a stats result: the functions referencing
    /// <paramref name="constant"/>, provided the indexed <paramref name="headerPath"/> defines it.
    /// Null when it does not, when the constant could not be derived, or on any failure.
    /// </summary>
    public static List<string>? ForStatsResult(SourceCodeService sourceCode, IConfiguration configuration, string? constant, string headerPath)
    {
        try
        {
            if (string.IsNullOrEmpty(constant)) return null;
            if (!IsDefinedIn(sourceCode.GetIndexedLines(headerPath), constant)) return null;
            return ForSourceCodeService(sourceCode, configuration).FindReferencingFunctions(constant)?.ToList();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="headerLines"/> hold a <c>#define</c> of <paramref name="constant"/>.</summary>
    internal static bool IsDefinedIn(string[]? headerLines, string constant)
    {
        if (headerLines == null || string.IsNullOrEmpty(constant)) return false;
        var defines = HeaderDefines.GetValue(headerLines, lines =>
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in lines)
            {
                var match = DefineRegex.Match(line);
                if (match.Success) names.Add(match.Groups[1].Value);
            }
            return names;
        });
        return defines.Contains(constant);
    }

    private static readonly Regex FirstLiteralRegex = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The content of the first string literal in <paramref name="text"/>, e.g. the name in a raw <c>A("Excalibur", …)</c> definition; null when none.</summary>
    internal static string? FirstStringLiteral(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = FirstLiteralRegex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    /* --- Constants as util/makedefs.c derives them --------------------------------- */

    /// <summary>
    /// <paramref name="name"/> as makedefs spells it: <c>a</c>-<c>z</c> upper-cased, <c>A</c>-<c>Z</c>
    /// kept, every other character a <c>_</c>.
    /// </summary>
    internal static string MakedefsUpper(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c >= 'a' && c <= 'z') sb.Append((char)(c - 'a' + 'A'));
            else if (c >= 'A' && c <= 'Z') sb.Append(c);
            else sb.Append('_');
        }
        return sb.ToString();
    }

    private static string Limit(string text, int length) => text.Length > length ? text.Substring(0, length) : text;

    /// <summary>The <c>ART_</c> constant of the artifact named <paramref name="name"/>: a leading <c>THE_</c>, then <c>PLATINUM_</c>, stripped, 36 characters at most after the prefix.</summary>
    public static string? ArtifactConstant(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string body = MakedefsUpper(name);
        if (body.StartsWith("THE_", StringComparison.Ordinal)) body = body.Substring(4);
        if (body.StartsWith("PLATINUM_", StringComparison.Ordinal)) body = body.Substring(9);
        return "ART_" + Limit(body, 36);
    }

    /// <summary>The <c>PM_</c> constant of the monster named <paramref name="name"/>, with <c>HUMAN_</c> inserted for an <c>S_HUMAN</c> were-creature.</summary>
    public static string? MonsterConstant(string? name, string? monsterClass)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        bool humanWere = string.Equals(monsterClass?.Trim(), "S_HUMAN", StringComparison.Ordinal)
            && name.StartsWith("were", StringComparison.Ordinal);
        return "PM_" + (humanWere ? "HUMAN_" : string.Empty) + MakedefsUpper(name);
    }

    /// <summary>
    /// The object constant of the item named <paramref name="name"/> in <paramref name="objectClass"/>:
    /// <c>WAN_</c>, <c>RIN_</c>, <c>POT_</c>, <c>SPE_</c> or <c>SCR_</c> and 36 characters after it, no
    /// prefix and 40 characters for every other class, and <c>FAKE_AMULET_OF_YENDOR</c> for the
    /// plastic amulet.
    /// </summary>
    public static string? ObjectConstant(string? name, string? objectClass, string? material)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(objectClass)) return null;
        string cls = objectClass.Trim();
        if (cls == "AMULET_CLASS" && string.Equals(material?.Trim(), "MAT_PLASTIC", StringComparison.Ordinal))
        {
            return "FAKE_AMULET_OF_YENDOR";
        }

        string prefix = cls switch
        {
            "WAND_CLASS" => "WAN_",
            "RING_CLASS" => "RIN_",
            "POTION_CLASS" => "POT_",
            "SPBOOK_CLASS" => "SPE_",
            "SCROLL_CLASS" => "SCR_",
            _ => string.Empty
        };
        return prefix + Limit(MakedefsUpper(name), prefix.Length > 0 ? 36 : 40);
    }
}
