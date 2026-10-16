namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Sentence case for the text of GnollBench documents: the first word of a sentence, a paragraph, a list item, a
/// table cell or the text after a label starts with a capital. Only a plain lowercase word is capitalized: ASCII
/// letters, optionally joined by an apostrophe or a hyphen to more lowercase letters (<c>none</c>, <c>it's</c>,
/// <c>well-known</c>). A word with a digit, a capital, an underscore or a dot, a symbol compound whose first part is
/// one letter (<c>p-value</c>, <c>t-test</c>), an abbreviation of <see cref="Abbreviations"/> and anything inside a
/// code span stay as written (<c>gpt-6</c>, <c>verboseMode</c>, <c>e.g.</c>, <c>`src/x.c`</c>). Leading Markdown
/// emphasis, opening quotes, brackets and parentheses are skipped to reach the word.
/// </summary>
internal static class BenchmarkReportSentenceCase
{
    /// <summary>Abbreviations after which a full stop does not end a sentence, each lowercase with its final dot.</summary>
    public static IReadOnlyList<string> Abbreviations { get; } = new[] { "e.g.", "i.e.", "etc.", "vs.", "cf.", "approx.", "incl.", "resp." };

    /// <summary>Skipped before a word: Markdown emphasis, opening quotes, brackets and parentheses.</summary>
    private const string Opening = "*_~\"'“‘([";

    /// <summary>Stripped from the end of a word before it is judged: closing punctuation, quotes, brackets and emphasis.</summary>
    private const string Closing = ".,;:!?)]}\"'”’*_~…";

    /// <summary>What may stand between a sentence's final stop and the whitespace after it.</summary>
    private const string AfterStop = ")]\"'”’*_~";

    private static readonly char[] ClosingChars = Closing.ToCharArray();

    private static readonly Regex PlainWord = new(@"^[a-z]+(?:['’-][a-z]+)*$", RegexOptions.CultureInvariant);

    private static readonly Regex SymbolCompound = new(@"^[a-z]-", RegexOptions.CultureInvariant);

    /// <summary>
    /// The text with the first word of the text, of every paragraph and of every sentence after <c>.</c>, <c>?</c> or
    /// <c>!</c> and whitespace capitalized. A full stop that ends an abbreviation, or a decimal point, ends no sentence;
    /// a code span is never read. Null or empty text is returned as is.
    /// </summary>
    public static string Apply(string text) => Apply(text, sentenceStart: true);

    /// <summary>
    /// As <see cref="Apply(string)"/>; with <paramref name="sentenceStart"/> false the text continues a sentence begun
    /// before it, so its first word is left alone, while every later sentence still starts with a capital.
    /// </summary>
    public static string Apply(string text, bool sentenceStart)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var chars = text.ToCharArray();
        bool changed = sentenceStart && CapitalizeAt(chars, 0);
        int i = 0;
        while (i < chars.Length)
        {
            char c = chars[i];
            if (c == '`')
            {
                i = AfterCodeSpan(chars, i);
                continue;
            }

            if (c is '.' or '?' or '!')
            {
                int j = i + 1;
                if (c == '.' && EndsAbbreviation(chars, i))
                {
                    i = j;
                    continue;
                }
                while (j < chars.Length && AfterStop.IndexOf(chars[j]) >= 0) j++;
                if (j < chars.Length && char.IsWhiteSpace(chars[j])) changed |= CapitalizeAt(chars, j);
                i = j;
                continue;
            }

            if (c == '\n')
            {
                int k = i + 1;
                while (k < chars.Length && chars[k] is ' ' or '\t' or '\r') k++;
                if (k < chars.Length && chars[k] == '\n') changed |= CapitalizeAt(chars, k);
            }
            i++;
        }
        return changed ? new string(chars) : text;
    }

    /// <summary>
    /// The text with its first word capitalized: a fragment that starts a sentence, such as a list item, a table cell
    /// or the text after a label. Leading whitespace is kept. Applying it twice changes nothing more.
    /// </summary>
    public static string UpperFirst(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var chars = text.ToCharArray();
        return CapitalizeAt(chars, 0) ? new string(chars) : text;
    }

    /// <summary>Capitalizes the word that starts at or after <paramref name="position"/>; false when it is not a plain lowercase word.</summary>
    private static bool CapitalizeAt(char[] chars, int position)
    {
        int p = position;
        while (p < chars.Length && char.IsWhiteSpace(chars[p])) p++;
        while (p < chars.Length && Opening.IndexOf(chars[p]) >= 0) p++;
        if (p >= chars.Length || chars[p] is < 'a' or > 'z') return false;

        int end = p;
        while (end < chars.Length && !char.IsWhiteSpace(chars[end]) && chars[end] is not ('—' or '–' or '`')) end++;
        string token = new(chars, p, end - p);

        if (Abbreviations.Any(a => token.StartsWith(a, StringComparison.Ordinal))) return false;
        string word = token.TrimEnd(ClosingChars);
        if (!PlainWord.IsMatch(word) || SymbolCompound.IsMatch(word)) return false;

        chars[p] = char.ToUpperInvariant(chars[p]);
        return true;
    }

    /// <summary>The full stop at <paramref name="dot"/> ends one of <see cref="Abbreviations"/> that starts a word.</summary>
    private static bool EndsAbbreviation(char[] chars, int dot)
    {
        foreach (string abbreviation in Abbreviations)
        {
            int start = dot + 1 - abbreviation.Length;
            if (start < 0) continue;
            if (start > 0 && char.IsLetter(chars[start - 1])) continue;
            if (string.Compare(new string(chars, start, abbreviation.Length), abbreviation, StringComparison.OrdinalIgnoreCase) == 0) return true;
        }
        return false;
    }

    /// <summary>The index after the code span that opens at <paramref name="start"/>; after its backticks alone when it is never closed.</summary>
    private static int AfterCodeSpan(char[] chars, int start)
    {
        int run = 0;
        while (start + run < chars.Length && chars[start + run] == '`') run++;

        int i = start + run;
        while (i < chars.Length)
        {
            if (chars[i] != '`')
            {
                i++;
                continue;
            }
            int closing = 0;
            while (i + closing < chars.Length && chars[i + closing] == '`') closing++;
            if (closing == run) return i + closing;
            i += closing;
        }
        return start + run;
    }
}
