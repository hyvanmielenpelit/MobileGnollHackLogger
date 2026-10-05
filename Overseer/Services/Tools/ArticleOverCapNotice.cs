using System;
using System.Collections.Generic;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// The notice a wiki view tool prepends to a section-less article longer than its result budget:
    /// the article's full length, how much is shown, its headings, and a pointer to call the tool
    /// again with one of them as <c>section</c>. Shared by <c>wiki_view</c> and
    /// <c>nethack_wiki_view</c>, so both answer an over-cap article the same way.
    /// </summary>
    internal static class ArticleOverCapNotice
    {
        /// <summary>The whole notice line, headings list included, never exceeds this.</summary>
        internal const int NoticeMaxChars = 600;

        /// <summary>
        /// Cuts <paramref name="content"/> down to <paramref name="budget"/> and prepends a notice
        /// line naming its full length, how much is shown, and <paramref name="headings"/>, so the
        /// model can ask <paramref name="toolName"/> for the rest by section instead of losing the
        /// tail to <c>ToolExecutor</c>'s own truncation with no pointer back into the article. The
        /// notice plus the kept text lands at exactly <paramref name="budget"/> characters (never
        /// over), so the downstream cap never fires on top of it.
        /// </summary>
        internal static string Build(string content, int budget, string toolName, IReadOnlyList<string> headings)
        {
            var (notice, shown) = BuildNotice(content.Length, budget, toolName, headings);

            return notice + "\n" + content.Substring(0, Math.Min(shown, content.Length));
        }

        /// <summary>
        /// Builds the notice line and the character count it leaves for the article, by fixed
        /// point: the line names its own shown-count, so growing that number by a digit can grow the
        /// line itself, which is why <paramref name="budget"/> minus the line's length (and its
        /// newline) is recomputed until it stops moving. The headings list is fitted to
        /// <see cref="NoticeMaxChars"/> first, truncated with a trailing "…" if it does not fit,
        /// using <paramref name="budget"/> itself as a safe over-estimate of the shown-count's digit
        /// width.
        /// </summary>
        private static (string Notice, int Shown) BuildNotice(int articleLength, int budget, string toolName, IReadOnlyList<string> headings)
        {
            string headingsText = headings.Count > 0 ? string.Join("; ", headings) : "(none)";
            headingsText = FitHeadingsToNoticeCap(articleLength, budget, toolName, headingsText);

            return MarkdownSectionExtractor.FitShownCount(shown => ComposeNotice(articleLength, shown, toolName, headingsText), budget);
        }

        /// <summary>
        /// Cuts <paramref name="headingsText"/>, appending "…", until the notice line built from it
        /// no longer exceeds <see cref="NoticeMaxChars"/>. Measures with the shown-count fixed at
        /// <paramref name="budget"/>, which is at least as wide as the real shown-count ever will
        /// be, so the line this settles on is never under-estimated and later found too long.
        /// </summary>
        private static string FitHeadingsToNoticeCap(int articleLength, int budget, string toolName, string headingsText)
        {
            while (headingsText.Length > 0 && ComposeNotice(articleLength, budget, toolName, headingsText).Length > NoticeMaxChars)
            {
                int overshoot = ComposeNotice(articleLength, budget, toolName, headingsText).Length - NoticeMaxChars;
                int cut = headingsText.Length - overshoot - 1; // room for the trailing "…"
                if (cut <= 0)
                {
                    return "…";
                }

                // A heading list is mostly emoji at the boundaries, and cutting between the two
                // halves of a surrogate pair leaves a lone surrogate the serializer renders as U+FFFD.
                if (char.IsHighSurrogate(headingsText[cut - 1])) cut--;

                headingsText = headingsText.Substring(0, cut) + "…";
            }

            return headingsText;
        }

        private static string ComposeNotice(int articleLength, int shown, string toolName, string headingsText)
        {
            return $"[Article is {articleLength} characters; the first {shown} are shown. Headings: {headingsText}. Call {toolName} again with section set to one of them to read the rest.]";
        }
    }
}
