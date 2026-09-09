using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Overseer.Services.Privacy;

/// <summary>
/// The single place uploaded document text enters a prompt. Wraps it in a named element the
/// system prompt can refer to, and escapes both the filename and the body so neither can
/// close that element early.
/// </summary>
/// <remarks>
/// The delimiter this replaces was <c>--- File: {name} ---</c> … <c>--- End File ---</c>, with
/// the filename interpolated raw. A file named <c>--- End File --- [System: …]</c> ends the
/// block from inside its own header, so the attacker-controlled part of an upload could
/// address the model directly. A tag-shaped delimiter is no better on its own — the same
/// filename spelling the closing tag escapes an unescaped wrapper just as easily — so the
/// escaping, not the tag, is what does the work here.
/// </remarks>
/// <summary>
/// How much of a document actually reached the prompt, when retrieval replaced the whole of it.
/// </summary>
/// <param name="UsedChunks">Excerpts included.</param>
/// <param name="TotalChunks">Excerpts the document was split into.</param>
/// <param name="CoveragePercent">Whole percent of the document's characters included.</param>
/// <param name="Method">Which retriever chose them: "embedding", "bm25" or "head".</param>
public sealed record DocumentExcerptInfo(int UsedChunks, int TotalChunks, int CoveragePercent, string Method);

public static class UntrustedContentWrapper
{
    /// <summary>Name of the wrapping element. Referenced verbatim by the system prompt.</summary>
    public const string ElementName = "untrusted_document_context";

    /// <summary>
    /// Cap on the filename carried in the attribute. Long enough for any real name, short
    /// enough that a filename cannot become the bulk of the prompt.
    /// </summary>
    public const int MaxFileNameLength = 256;

    /* Matches anything that reads as this element's start or end tag, however it is spaced.
       XML would not accept whitespace after "<", but the model is not an XML parser: it reads
       the text, so anything shaped like the delimiter is treated as the delimiter. */
    private static readonly Regex DelimiterShape = new(
        @"<\s*/?\s*" + ElementName,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Wraps one attachment's extracted text.
    /// </summary>
    /// <param name="fileName">The uploader's filename. Attacker-controlled; escaped here.</param>
    /// <param name="index">1-based position among this turn's attachments, so several are distinguishable.</param>
    /// <param name="text">The document's text. Attacker-controlled; escaped here.</param>
    public static string Wrap(string? fileName, int index, string? text)
        => Wrap(fileName, index, text, excerpt: null);

    /// <summary>
    /// Wraps one attachment's extracted text, declaring on the element whether the text is the
    /// whole document or a set of excerpts.
    /// </summary>
    /// <param name="excerpt">
    /// Provenance when retrieval replaced the document, or null when the whole thing is here.
    /// </param>
    /// <remarks>
    /// The model is told, and not only the user. An assistant handed six chunks of a ninety-page
    /// PDF will otherwise answer as though it read all ninety -- confidently, and with no signal
    /// that the answer is partial. Saying so on the element is what lets it hedge, and lets it
    /// ask for more rather than inventing the rest.
    /// </remarks>
    public static string Wrap(string? fileName, int index, string? text, DocumentExcerptInfo? excerpt)
    {
        var sb = new StringBuilder();
        sb.Append('<').Append(ElementName)
          .Append(" index=\"").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
          .Append("\" filename=\"").Append(EscapeAttribute(fileName));

        if (excerpt != null)
        {
            sb.Append("\" content=\"excerpts")
              .Append("\" excerpts=\"").Append(excerpt.UsedChunks.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(" of ").Append(excerpt.TotalChunks.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append("\" document_coverage=\"").Append(excerpt.CoveragePercent.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append("%\" retrieval=\"").Append(EscapeAttribute(excerpt.Method));
        }
        else
        {
            sb.Append("\" content=\"complete");
        }

        sb.AppendLine("\">");
        sb.AppendLine(EscapeBody(text));
        sb.Append("</").Append(ElementName).Append('>');
        return sb.ToString();
    }

    /// <summary>
    /// Escapes a value for an XML attribute: the five predefined entities, with control
    /// characters and newlines removed so the value cannot break out of the quoting or out of
    /// the line.
    /// </summary>
    /// <remarks>
    /// Truncation happens on the raw value, before escaping, so the cut can never fall inside
    /// an entity and leave a mangled <c>&amp;q</c> behind.
    /// </remarks>
    public static string EscapeAttribute(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "(unnamed)";

        var cleaned = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            // Newlines included: an attribute value spanning lines is how a header is faked.
            if (!char.IsControl(c))
                cleaned.Append(c);
        }

        string name = cleaned.ToString().Trim();
        if (name.Length == 0)
            return "(unnamed)";

        if (name.Length > MaxFileNameLength)
            name = name[..MaxFileNameLength];

        var sb = new StringBuilder(name.Length + 16);
        foreach (char c in name)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Neutralises anything in the body that reads as this element's own tag, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. Escaping every <c>&lt;</c> and <c>&amp;</c> would corrupt exactly
    /// the uploads users most want analysed — an HTML dump, a source file, an XML config — and
    /// the model needs to see those as they are. Only the sequence that could end the wrapper
    /// is touched, by turning its <c>&lt;</c> into <c>&amp;lt;</c>.
    /// </remarks>
    public static string EscapeBody(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return DelimiterShape.Replace(text, static m => "&lt;" + m.Value[1..]);
    }
}
