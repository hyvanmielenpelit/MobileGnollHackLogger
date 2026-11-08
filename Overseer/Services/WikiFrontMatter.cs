using System;
using System.Text.RegularExpressions;

namespace Overseer.Services;

/// <summary>
/// The YAML front matter block a GnollHack wiki page may open with. Gollum removes it before
/// rendering, so the wiki never shows it; this removes it the same way.
/// </summary>
internal static class WikiFrontMatter
{
    // gollum-lib's Gollum::Filter::YAML pattern. \s* also takes the CR of a CRLF line end.
    private static readonly Regex Block = new(
        @"\A(---\s*\n.*?\n?)^((---|\.\.\.)\s*$\n?)",
        RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>The text without a leading front matter block; unchanged when it has none.</summary>
    public static string Strip(string text) => Block.Replace(text, string.Empty, 1);
}
