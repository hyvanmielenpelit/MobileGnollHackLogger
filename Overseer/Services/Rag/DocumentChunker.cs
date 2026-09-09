using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Rag;

/// <summary>One chunk of a document, with enough provenance to cite it.</summary>
public sealed record DocumentChunk
{
    /// <summary>Zero-based position in the document's chunk sequence.</summary>
    public int Index { get; init; }

    public string Text { get; init; } = "";

    /// <summary>Estimated tokens in <see cref="Text"/>.</summary>
    public int TokenCount { get; init; }

    /// <summary>Character offset of <see cref="Text"/> in the source, for the caller to report coverage.</summary>
    public int SourceOffset { get; init; }
}

/// <summary>
/// Splits an uploaded document into overlapping chunks, and decides whether it needs splitting
/// at all.
/// </summary>
/// <remarks>
/// The three dials are policy rather than correctness: a document chunked slightly differently
/// still answers the same question, so every setting is read defensively and every out-of-range
/// value resolves to something workable instead of stopping the application.
/// </remarks>
public sealed class DocumentChunker
{
    /// <summary>Characters per token, the constant behind <see cref="EstimateTokens"/>.</summary>
    private const int CharsPerToken = 4;

    public DocumentChunker(IConfiguration configuration)
    {
        var section = configuration.GetSection("RagSettings");

        TargetTokens = Math.Max(8, RagConfig.ReadInt(section, "ChunkTargetTokens", 650));

        /* Overlap wider than half a chunk would repeat more of the document than it adds, and at
           a full chunk's width it would stop the walk advancing at all. */
        OverlapTokens = Math.Clamp(RagConfig.ReadInt(section, "ChunkOverlapTokens", 80), 0, TargetTokens / 2);

        DirectIngestionMaxTokens = Math.Max(0, RagConfig.ReadInt(section, "DirectIngestionMaxTokens", 12000));
    }

    /// <summary><c>RagSettings:ChunkTargetTokens</c>, default 650.</summary>
    public int TargetTokens { get; }

    /// <summary><c>RagSettings:ChunkOverlapTokens</c>, default 80.</summary>
    public int OverlapTokens { get; }

    /// <summary><c>RagSettings:DirectIngestionMaxTokens</c>, default 12000.</summary>
    public int DirectIngestionMaxTokens { get; }

    /// <summary>Rough token count: one token per four characters, rounded up.</summary>
    /// <remarks>
    /// Deliberately an estimate rather than a real tokenizer pass. Both dials it feeds — the
    /// direct-ingestion threshold and the chunk size — are policy, not correctness boundaries:
    /// being fifteen percent out moves a chunk edge, it does not produce a wrong answer. Running
    /// a real tokenizer over a ninety-page document merely to decide where to split it costs far
    /// more than the precision buys. The four-characters scale is the same one
    /// <c>ChatService.EstimateTokens</c> uses, so a document measured here and a prompt measured
    /// there are on one ruler; this one rounds up, so a short non-empty string is never worth
    /// zero tokens.
    /// </remarks>
    public static int EstimateTokens(string? text)
        => string.IsNullOrEmpty(text) ? 0 : (text.Length + CharsPerToken - 1) / CharsPerToken;

    /// <summary>True when the document is small enough to send whole.</summary>
    /// <remarks>
    /// Retrieval is not free: it adds latency, and it hands the model fragments instead of a
    /// document, so the answer reads as fragments too. Against a context window measured in
    /// hundreds of thousands of tokens, chunking a two-page report to spare the model 1,500
    /// tokens loses on every axis — privacy included, because the excerpts that do get sent are
    /// the relevant ones either way. Below the threshold the document goes to the model intact.
    /// </remarks>
    public bool FitsDirectIngestion(string? text) => EstimateTokens(text) <= DirectIngestionMaxTokens;

    /// <summary>
    /// Splits on structure first and length second, with overlap. Never returns an empty chunk,
    /// and never loses text: the chunks, in order, cover every character of the input.
    /// </summary>
    /// <remarks>
    /// Boundaries are chosen by preference — a paragraph break, then a sentence end, then a line
    /// break, then any whitespace, and only mid-word when the window holds none of those. A
    /// mid-word boundary is a real quality loss: it splits the very term a query would have
    /// matched and leaves both halves matching nothing, so it is the last resort rather than the
    /// arithmetic default. Consecutive chunks overlap by <see cref="OverlapTokens"/>, so a
    /// sentence sitting on a boundary still appears whole in one of them.
    /// </remarks>
    public IReadOnlyList<DocumentChunk> Chunk(string? text)
    {
        var chunks = new List<DocumentChunk>();
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        int targetChars = TargetTokens * CharsPerToken;
        int maxChars = targetChars + targetChars / 4;
        int minChars = Math.Max(1, targetChars / 2);
        int overlapChars = OverlapTokens * CharsPerToken;

        int start = 0;
        while (start < text.Length)
        {
            /* Window sizes are measured from the first non-whitespace character, so a long run of
               blank lines is carried by the chunk that follows it instead of consuming a whole
               chunk on its own. */
            int contentStart = start;
            while (contentStart < text.Length && char.IsWhiteSpace(text[contentStart]))
                contentStart++;

            if (contentStart >= text.Length)
            {
                // Nothing but whitespace left; it belongs to the chunk before it.
                ExtendLast(chunks, text, text.Length);
                break;
            }

            int end = text.Length - contentStart <= maxChars
                ? text.Length
                : FindBreak(text, contentStart + minChars, contentStart + maxChars);

            string slice = text.Substring(start, end - start);
            chunks.Add(new DocumentChunk
            {
                Index = chunks.Count,
                Text = slice,
                TokenCount = EstimateTokens(slice),
                SourceOffset = start
            });

            if (end >= text.Length)
                break;

            int next = end - overlapChars;
            if (next <= start)
            {
                // An overlap at least as wide as the chunk would not advance; skip it this step.
                next = end;
            }
            else
            {
                /* The repeated text begins on a word boundary, so the overlap does not open with
                   half a word that matches nothing. */
                int snap = next;
                while (snap < end && !char.IsWhiteSpace(text[snap]))
                    snap++;
                if (snap < end)
                    next = snap;
            }

            start = next;
        }

        return chunks;
    }

    /// <summary>
    /// The best boundary in <paramref name="min"/> (exclusive) to <paramref name="max"/>
    /// (inclusive), by preference order. Falls back to <paramref name="max"/> — a mid-word cut —
    /// when the window holds no boundary at all.
    /// </summary>
    private static int FindBreak(string text, int min, int max)
    {
        // A blank line is the strongest statement the author made about where a thought ends.
        for (int i = max; i > min; i--)
        {
            if (!IsNewLine(text[i - 1]))
                continue;

            int newLines = 0;
            int j = i - 1;
            while (j >= 0 && (IsNewLine(text[j]) || text[j] == ' ' || text[j] == '\t'))
            {
                if (IsNewLine(text[j]))
                    newLines++;
                j--;
            }

            if (newLines >= 2)
                return i;
        }

        for (int i = max; i > min; i--)
        {
            if (IsSentenceEnd(text, i))
                return i;
        }

        for (int i = max; i > min; i--)
        {
            if (IsNewLine(text[i - 1]))
                return i;
        }

        for (int i = max; i > min; i--)
        {
            if (char.IsWhiteSpace(text[i - 1]))
                return i;
        }

        return max;
    }

    /// <summary>Whether <paramref name="end"/> sits just after a sentence's terminating punctuation.</summary>
    private static bool IsSentenceEnd(string text, int end)
    {
        if (end <= 0)
            return false;

        // A closing quote or bracket may follow the punctuation, as in: guard the gate."
        // The literals are escaped so the file stays ASCII where behaviour depends on it.
        int i = end - 1;
        while (i > 0 && text[i] is '"' or '\'' or ')' or ']' or '\u201d' or '\u2019' or '\u00bb')
            i--;

        if (text[i] is not ('.' or '!' or '?' or '\u2026'))
            return false;

        // A full stop inside "3.5" or "e.g." is not a sentence end; a following space says it is.
        return end >= text.Length || char.IsWhiteSpace(text[end]);
    }

    private static bool IsNewLine(char c) => c is '\n' or '\r';

    /// <summary>Grows the last chunk to <paramref name="end"/>, absorbing a trailing run of whitespace.</summary>
    private static void ExtendLast(List<DocumentChunk> chunks, string text, int end)
    {
        if (chunks.Count == 0)
            return;

        var last = chunks[^1];
        if (end <= last.SourceOffset + last.Text.Length)
            return;

        string grown = text.Substring(last.SourceOffset, end - last.SourceOffset);
        chunks[^1] = last with { Text = grown, TokenCount = EstimateTokens(grown) };
    }
}

/// <summary>Reads the <c>RagSettings</c> section without letting a typo stop the application.</summary>
internal static class RagConfig
{
    /// <summary>
    /// A missing, empty or unparseable setting reads as <paramref name="fallback"/>.
    /// </summary>
    /// <remarks>
    /// Parsed by hand rather than through <c>ConfigurationBinder.GetValue</c>, which throws on a
    /// value it cannot convert — so <c>"ChunkTargetTokens": "six hundred"</c> would take the
    /// application down from inside a DI constructor, with an error naming DI rather than the
    /// setting.
    /// </remarks>
    public static int ReadInt(IConfiguration section, string key, int fallback)
        => int.TryParse(section[key]?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
}
