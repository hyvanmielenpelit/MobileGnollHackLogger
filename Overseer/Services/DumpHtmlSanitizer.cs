using System.Net;
using System.Text.RegularExpressions;

namespace Overseer.Services;

/// <summary>
/// Converts GnollHack dump HTML into plain text for an LLM while preserving the
/// line structure the C engine wrote. The engine emits a real newline after most
/// block tags and after every map row, so block-level tags consume that newline
/// and inline tags are removed without inserting anything.
///
/// The reference implementation is SanitizeDumpHtml() in the GnollHack client
/// (win/win32/xpl/GnollHackX/GnollHackX/Pages/Game/OverseerPage.xaml.cs), and it
/// runs for every snapshot a current app sends: such an app posts the flattened
/// text in SnapshotText and the raw dump in SnapshotHtml, and the server reads
/// the latter only when the former is absent or blank.
///
/// Sanitize() is therefore for app versions that upload raw dump HTML alone, and
/// must keep producing what the client's function produces for the same input.
/// The two are byte-identical for the same dump - the client has the thead / tr
/// block-tag handling and the U+00A0 step as well. Keep them in sync.
///
/// Step 8 normalizes U+00A0 back to an ASCII space to save tokens. It is safe
/// precisely because every collapse has already run, so one U+00A0 becomes
/// exactly one space and the map's column counts are unchanged.
/// </summary>
public static class DumpHtmlSanitizer
{
    /* 1. Drop <script>/<style> blocks including their contents. dump_open_log_ai()
          writes a <style> block; stripping only the tags leaves CSS as text. */
    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /* 2. Table cells keep a column separator. html_dump_str() writes
          <td>a</td><td>b</td> with no whitespace between them. The two spaces
          written here are collapsed to one by step 5 - that is intentional and
          matches the client; the point is that the columns do not run together. */
    private static readonly Regex TableCellEndRegex = new(
        @"</(td|th)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /* 3a. Closing block tags become the line break, CONSUMING the newline the
           engine already wrote after them. Without the trailing [ \t]*\r?\n? this
           doubles every line and blank-lines the whole map. */
    private static readonly Regex BlockCloseRegex = new(
        @"(<br\s*/?>|</(p|div|section|li|tr|h[1-6]|ul|ol|table|tbody|thead|theader|pre)\s*>)"
        + @"[ \t]*\r?\n?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /* 3b. Opening block tags contribute no break of their own and must take the
           engine's following newline with them. */
    private static readonly Regex BlockOpenRegex = new(
        @"<(p|div|section|ul|ol|table|tbody|thead|theader|tr|pre|li|h[1-6])\b[^>]*>[ \t]*\r?\n?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /* 4. Anything left is an inline tag and may start mid-word, so it is removed
          without inserting anything. Crucially this does NOT consume a following
          newline: a coloured map cell ends a row as ""</span>\n"" and that newline
          is the row break. */
    private static readonly Regex RemainingTagRegex = new(
        "<[^>]*>", RegexOptions.Compiled);

    /* 5. Collapse horizontal runs ONLY - never newlines. Deliberately [ \t]+ and
          not [^\S\r\n]+: .NET's \s matches U+00A0, so the latter would flatten the
          map if step 7 were ever moved ahead of this one. */
    private static readonly Regex HorizontalRunRegex = new(
        @"[ \t]+", RegexOptions.Compiled);

    /* 6. Tidy trailing horizontal whitespace and excess blank lines. */
    private static readonly Regex TrailingSpaceRegex = new(
        @"[ \t]+\n", RegexOptions.Compiled);
    private static readonly Regex BlankLineRunRegex = new(
        @"\n{3,}", RegexOptions.Compiled);

    /// <summary>
    /// Final normalization applied to text that is already flattened — either by
    /// Sanitize() here, or by the client's SanitizeDumpHtml() before a
    /// refresh_snapshot round trip. Unifies CRLF and lone CR line endings to LF, trims
    /// trailing spaces per line, collapses runs of blank lines to one, turns U+00A0 into
    /// a space and trims the whole. Idempotent: running it twice changes nothing.
    /// </summary>
    public static string NormalizeFlattenedText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = TrailingSpaceRegex.Replace(text, "\n");
        text = BlankLineRunRegex.Replace(text, "\n\n");
        text = text.Replace('\u00A0', ' ');
        return text.Trim();
    }

    /* Upper bound for snapshot text that arrives already flattened. The app truncates at
       60,000 characters and appends a short marker; the slack covers the marker. */
    public const int MaxFlattenedSnapshotChars = 60200;

    /// <summary>
    /// Caps and normalizes snapshot text the client has already flattened, whether it
    /// arrives as SnapshotText on session create or through refresh_snapshot. Returns an
    /// empty string for null or whitespace-only input.
    /// </summary>
    public static string PrepareFlattenedSnapshot(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (text.Length > MaxFlattenedSnapshotChars)
        {
            /* Never cut between the halves of a surrogate pair: a lone surrogate can make a
               JSON serializer or the SignalR transport throw. */
            int cap = MaxFlattenedSnapshotChars;
            if (char.IsHighSurrogate(text[cap - 1]))
                cap--;
            text = text.Substring(0, cap);
        }

        return NormalizeFlattenedText(text);
    }

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        string text = ScriptStyleRegex.Replace(html, " ");
        text = TableCellEndRegex.Replace(text, "  ");
        text = BlockCloseRegex.Replace(text, "\n");
        text = BlockOpenRegex.Replace(text, "");
        text = RemainingTagRegex.Replace(text, "");
        text = HorizontalRunRegex.Replace(text, " ");

        /* 7. Decode entities LAST. During step 5, ""&nbsp;"" is still literal text and
              therefore survives any collapse pattern; only here does it become
              U+00A0 and hold the map's column alignment. Decoding last also means
              no ""&lt;"" can be mistaken for a tag by step 4. */
        text = WebUtility.HtmlDecode(text);

        /* 8. Normalize U+00A0 back to ASCII space now that all collapsing has
              finished. One U+00A0 becomes exactly one space, so column counts are
              unchanged; plain spaces cost fewer tokens. Extracted to
              NormalizeFlattenedText so client-sanitized text (Path B) shares the
              same final normalization as raw-HTML-sanitized text (Path A). */
        return NormalizeFlattenedText(text);
    }
}