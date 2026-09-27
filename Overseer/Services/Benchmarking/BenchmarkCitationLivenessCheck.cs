namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Notes a claim verification whose citation, <c>src/&lt;file&gt;.c:&lt;line&gt;</c>, points into a
/// GnollHack function that nothing references: the function enclosing the cited line — a column-0
/// definition whose parameter list is followed by a body, so a macro-table row such as
/// <c>SCROLL(…),</c> is no function — has no <c>name(</c> call and no value reference to
/// <c>name</c> (a function pointer passed or stored) in the indexed GnollHack source outside its own
/// definition, a prototype, a <c>#define</c>, comments and <c>#if 0</c> regions. Such a verification
/// is read as Indeterminate for every flag and count
/// (<see cref="BenchmarkClaimVerification.EffectiveVerdict"/>); its stored verdict is not touched.
///
/// A function reached only through a macro that builds its name also has no reference, so the
/// note never argues the opposite verdict — it only withdraws the cited code as evidence. A citation
/// with any other reference (wiki, board, another file) gets no note, nor does a NetHack citation.
///
/// A citation of a <c>src/&lt;file&gt;</c> the index does not carry at all — never indexed, or
/// dropped for exceeding the indexer's per-file size limit — gets the same Indeterminate treatment
/// under a note naming the file rather than a function.
///
/// A citation naming a <c>src/</c> or <c>include/</c> file but no line — including one that names a
/// symbol instead of a line number — gets a note saying so, unless the rest of the citation also
/// names a wiki page or a board line. A single-line, unranged reference whose cited line is itself
/// only a column-0 function definition — nothing inside the body — gets a note naming the function
/// instead of the usual liveness check; a range starting on that same line is exempt, since it also
/// reaches inside the body.
///
/// A single-line, unranged reference to a <c>src/*.c</c> or <c>include/*.h</c> line inside a
/// <c>#define</c> header — the <c>#define</c> line, or for a function-like macro one of the
/// backslash-continued lines up to the one closing its parameter list — gets a note naming the
/// macro, the same way. A line of the macro's body falls through to the other checks. An
/// <c>include/*.h</c> reference is checked for nothing else.
///
/// A reference whose cited line, or every line of its cited range, is blank once comments are
/// removed gets a note saying so.
///
/// A line written in prose after the file — <c>src/x.c at line 12</c>, <c>src/x.c lines 10-20</c>,
/// <c>src/x.c L12</c> within 40 characters with no other file between — is read as
/// <c>src/x.c:12</c>; so is a free-standing <c>line 12</c> when the citation names exactly one
/// source file.
///
/// A verification that already carries a note keeps it.
///
/// Liveness, definitions and comment stripping come from <see cref="SourceLivenessIndex"/>. Pure
/// over the corpus it is given; any failure yields no note.
/// </summary>
public sealed class BenchmarkCitationLivenessCheck
{
    private readonly SourceLivenessIndex _index;

    /// <param name="corpus">
    /// The indexed GnollHack source: repository-relative, forward-slashed path to the file's lines.
    /// Called once per check; a new dictionary instance means a new corpus.
    /// </param>
    public BenchmarkCitationLivenessCheck(Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> corpus)
    {
        _index = new SourceLivenessIndex(corpus ?? throw new ArgumentNullException(nameof(corpus)));
    }

    private BenchmarkCitationLivenessCheck(SourceLivenessIndex index)
    {
        _index = index;
    }

    /// <summary>A check over the GnollHack repository <paramref name="sourceCode"/> indexes, read from disk.</summary>
    public static BenchmarkCitationLivenessCheck ForSourceCodeService(SourceCodeService sourceCode, int maxFileSizeKB)
        => new(SourceLivenessIndex.ForSourceCodeService(sourceCode, maxFileSizeKB));

    /// <summary>A check over the corpus <paramref name="index"/> reads.</summary>
    public static BenchmarkCitationLivenessCheck ForLivenessIndex(SourceLivenessIndex index)
        => new(index ?? throw new ArgumentNullException(nameof(index)));

    public static string NoteText(string functionName) => $"cited function {functionName} has no live call site";

    public static string MissingFileNoteText(string path) => $"cited file {path} is not in the indexed source";

    public static string LinelessCitationNoteText(string path) => $"cited file {path} without a line";

    public static string DefinitionLineNoteText(string path, int line, string functionName)
        => $"cited line {path}:{line} is only the definition line of {functionName}";

    public static string MacroDefinitionNoteText(string path, int line, string macroName)
        => $"cited line {path}:{line} is only the definition of macro {macroName}";

    public static string BlankLineNoteText(string path, int line)
        => $"cited line {path}:{line} is blank or a comment";

    public static string BlankRangeNoteText(string path, int firstLine, int lastLine)
        => $"cited lines {path}:{firstLine}-{lastLine} are blank or comments";

    /// <summary>A <c>src/*.c</c> or <c>include/*.h</c> line or line range; group 1 is the path.</summary>
    private static readonly Regex SourceReferenceRegex = new(
        @"(?<![\w/.-])(src/[\w./-]+?\.c|include/[\w./-]+?\.h):(\d+)(?:\s*[-–]\s*(\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DefineRegex = new(@"^\s*#\s*define\s+([A-Za-z_]\w*)", RegexOptions.Compiled);

    private const int MaxMacroHeaderLines = 200;

    /// <summary>A <c>src/</c> or <c>include/</c> file named without a line: no <c>:&lt;digit&gt;</c> follows it.</summary>
    private static readonly Regex LinelessSourceFileRegex = new(
        @"(?<![\w/.-])((?:src|include)/[\w./-]+?\.(?:c|h))\b(?!\s*:\s*\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A line in prose: <c>at line 12</c>, <c>lines 10-20</c>, <c>L12</c>, <c>line 10 to 20</c>. The
    /// line is group <c>first</c>, a range end group <c>last</c>.
    /// </summary>
    private const string ProseLinePattern =
        @"\b(?i:(?:at|on|in|near|around)\s+)?(?:(?i:lines?)\s*|L)(?<first>\d+)(?:\s*(?:-|–|(?i:to|through))\s*(?<last>\d+))?\b";

    /// <summary>
    /// A source file followed within 40 characters, with no other file and no <c>board</c> or
    /// <c>wiki</c> between, by a <see cref="ProseLinePattern"/> line; group 1 is the path,
    /// <c>gap</c> the text between.
    /// </summary>
    private static readonly Regex ProseLineReferenceRegex = new(
        @"(?<![\w/.-])(src/[\w./-]+?\.c|include/[\w./-]+?\.h)\b(?!\s*:\s*\d)"
            + @"(?<gap>(?:(?!(?:[\w-]+/)+[\w.-]*\.(?:c|h)\b|\b[\w-]+\.(?:c|h)\b|\b(?i:board|wiki)\b)[^\r\n]){0,40}?)"
            + ProseLinePattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FreeLineReferenceRegex = new(
        ProseLinePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BoardOrWikiRegex = new(
        @"\b(?:board|wiki)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OtherEvidenceRegex = new(
        @"\bwiki\s*:|\bboard\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OtherReferenceRegex = new(
        @"\bwiki\s*:|\bboard\s*:|[\w-]+/[\w./-]*\.(?:c|h|txt|des|md|cs)\b|\b[\w-]+\.(?:c|h)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The verifications with <see cref="BenchmarkClaimVerification.CitationNote"/> set where it applies.</summary>
    public List<BenchmarkClaimVerification> Annotate(IReadOnlyList<BenchmarkClaimVerification> verifications)
    {
        var result = new List<BenchmarkClaimVerification>(verifications.Count);
        foreach (var v in verifications)
        {
            if (!string.IsNullOrWhiteSpace(v.CitationNote))
            {
                result.Add(v);
                continue;
            }

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
        text = NormalizeProseLineReferences(text);

        var references = SourceReferenceRegex.Matches(text).Cast<Match>().ToList();
        if (references.Count == 0)
        {
            var linelessFiles = LinelessSourceFileRegex.Matches(text).Cast<Match>().ToList();
            if (linelessFiles.Count == 0) return null;
            if (OtherEvidenceRegex.IsMatch(LinelessSourceFileRegex.Replace(text, " "))) return null;
            return LinelessCitationNoteText(linelessFiles[0].Groups[1].Value);
        }
        if (OtherReferenceRegex.IsMatch(SourceReferenceRegex.Replace(text, " "))) return null;

        var view = _index.GetView();
        var notes = new List<string>();
        foreach (var reference in references)
        {
            string path = reference.Groups[1].Value;
            if (!int.TryParse(reference.Groups[2].Value, out int line)) return null;
            bool hasRange = reference.Groups[3].Success;
            bool isHeader = path.StartsWith("include/", StringComparison.Ordinal);

            if (!view.Stripped.TryGetValue(path, out var lines))
            {
                if (isHeader) return null;
                string missingNote = MissingFileNoteText(path);
                if (!notes.Contains(missingNote, StringComparer.Ordinal)) notes.Add(missingNote);
                continue;
            }

            if (line >= 1 && line <= lines.Length)
            {
                int lastLine = hasRange && int.TryParse(reference.Groups[3].Value, out int rangeEnd) ? rangeEnd : line;
                lastLine = Math.Min(Math.Max(lastLine, line), lines.Length);

                bool blank = true;
                for (int row = line - 1; row < lastLine && blank; row++)
                {
                    blank = string.IsNullOrWhiteSpace(lines[row]);
                }

                if (blank)
                {
                    string blankNote = lastLine > line ? BlankRangeNoteText(path, line, lastLine) : BlankLineNoteText(path, line);
                    if (!notes.Contains(blankNote, StringComparer.Ordinal)) notes.Add(blankNote);
                    continue;
                }
            }

            if (!hasRange && line >= 1 && line <= lines.Length)
            {
                string? macroName = MacroDefinedAt(lines, line - 1);
                if (macroName != null)
                {
                    string macroNote = MacroDefinitionNoteText(path, line, macroName);
                    if (!notes.Contains(macroNote, StringComparer.Ordinal)) notes.Add(macroNote);
                    continue;
                }
            }

            if (isHeader) return null;

            if (!hasRange && line >= 1 && line <= lines.Length)
            {
                string? definitionName = SourceLivenessIndex.DefinitionNameAt(lines, line - 1, lines[line - 1]);
                if (definitionName != null)
                {
                    string definitionNote = DefinitionLineNoteText(path, line, definitionName);
                    if (!notes.Contains(definitionNote, StringComparer.Ordinal)) notes.Add(definitionNote);
                    continue;
                }
            }

            string? name = SourceLivenessIndex.EnclosingFunction(view, path, line);
            if (name == null || view.IsLive(name)) return null;
            string functionNote = NoteText(name);
            if (!notes.Contains(functionNote, StringComparer.Ordinal)) notes.Add(functionNote);
        }

        return notes.Count == 0 ? null : string.Join("; ", notes);
    }

    /// <summary>
    /// <paramref name="text"/> with prose line references rewritten as <c>path:N</c> or
    /// <c>path:N-M</c>: a <see cref="ProseLineReferenceRegex"/> match keeps the text between the file
    /// and its line after the reference; otherwise, when no <c>path:N</c> reference results, the
    /// text names exactly one lineless source file, holds exactly one prose line and mentions no
    /// board or wiki, that line is removed and given to every mention of the file.
    /// </summary>
    internal static string NormalizeProseLineReferences(string text)
    {
        string result = ProseLineReferenceRegex.Replace(
            text, m => FormatReference(m.Groups[1].Value, m.Groups["first"].Value, m.Groups["last"]) + m.Groups["gap"].Value);
        if (SourceReferenceRegex.IsMatch(result) || BoardOrWikiRegex.IsMatch(result)) return result;

        var files = LinelessSourceFileRegex.Matches(result).Cast<Match>()
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        if (files.Count != 1) return result;

        string blanked = LinelessSourceFileRegex.Replace(result, m => new string(' ', m.Length));
        var lines = FreeLineReferenceRegex.Matches(blanked);
        if (lines.Count != 1) return result;

        var free = lines[0];
        string reference = FormatReference(files[0], free.Groups["first"].Value, free.Groups["last"]);
        return LinelessSourceFileRegex.Replace(result.Remove(free.Index, free.Length), _ => reference);
    }

    private static string FormatReference(string path, string first, Group last)
        => last.Success ? $"{path}:{first}-{last.Value}" : $"{path}:{first}";

    /// <summary>
    /// The name of the macro whose <c>#define</c> header holds line <paramref name="row"/> (0-based):
    /// the <c>#define</c> line itself, or — when every line from it up to <paramref name="row"/> ends
    /// in <c>\</c> — a line no later than <see cref="ParameterListEnd"/>. Null otherwise, including on
    /// a line of the macro's body.
    /// </summary>
    private static string? MacroDefinedAt(string[] lines, int row)
    {
        int definitionRow = -1;
        var define = DefineRegex.Match(lines[row]);
        if (define.Success)
        {
            definitionRow = row;
        }
        else
        {
            int first = Math.Max(0, row - MaxMacroHeaderLines);
            for (int i = row - 1; i >= first; i--)
            {
                if (!lines[i].TrimEnd().EndsWith('\\')) return null;

                define = DefineRegex.Match(lines[i]);
                if (define.Success)
                {
                    definitionRow = i;
                    break;
                }
            }
        }

        if (definitionRow < 0) return null;
        return row <= ParameterListEnd(lines, definitionRow, define) ? define.Groups[1].Value : null;
    }

    /// <summary>
    /// The row holding the closing <c>)</c> of a function-like macro's parameter list — the
    /// character right after the name is <c>(</c> — scanning forward over backslash-continued rows;
    /// <paramref name="definitionRow"/> for an object-like macro.
    /// </summary>
    private static int ParameterListEnd(string[] lines, int definitionRow, Match define)
    {
        int column = define.Groups[1].Index + define.Groups[1].Length;
        string first = lines[definitionRow];
        if (column >= first.Length || first[column] != '(') return definitionRow;

        int last = Math.Min(lines.Length - 1, definitionRow + MaxMacroHeaderLines);
        int depth = 0;
        for (int r = definitionRow; r <= last; r++)
        {
            string text = lines[r];
            for (int c = r == definitionRow ? column : 0; c < text.Length; c++)
            {
                if (text[c] == '(')
                {
                    depth++;
                }
                else if (text[c] == ')')
                {
                    depth--;
                    if (depth == 0) return r;
                }
            }

            if (!text.TrimEnd().EndsWith('\\')) return r;
        }

        return last;
    }
}
