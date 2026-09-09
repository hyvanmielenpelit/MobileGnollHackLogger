using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Rag;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class DocumentChunkerTests
{
    private static DocumentChunker Chunker(int? target = null, int? overlap = null, int? direct = null)
    {
        var settings = new Dictionary<string, string?>();
        if (target.HasValue)
            settings["RagSettings:ChunkTargetTokens"] = target.Value.ToString(CultureInfo.InvariantCulture);
        if (overlap.HasValue)
            settings["RagSettings:ChunkOverlapTokens"] = overlap.Value.ToString(CultureInfo.InvariantCulture);
        if (direct.HasValue)
            settings["RagSettings:DirectIngestionMaxTokens"] = direct.Value.ToString(CultureInfo.InvariantCulture);

        return new DocumentChunker(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    /// <summary>Ordinary prose: sentences that end in full stops, grouped into paragraphs.</summary>
    private static string Document(int paragraphs = 12)
    {
        var text = new StringBuilder();
        for (int p = 0; p < paragraphs; p++)
        {
            for (int s = 0; s < 5; s++)
                text.Append($"Paragraph {p} sentence {s} describes the ancient temple and its wardens. ");

            text.Append("\n\n");
        }

        return text.ToString();
    }

    // ── The dials ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultDialsAreTheDocumentedOnes()
    {
        var chunker = Chunker();

        Assert.Equal(650, chunker.TargetTokens);
        Assert.Equal(80, chunker.OverlapTokens);
        Assert.Equal(12000, chunker.DirectIngestionMaxTokens);
    }

    [Fact]
    public void AnUnparseableSettingFallsBackToItsDefaultRatherThanThrowing()
    {
        /* ConfigurationBinder.GetValue throws on a value it cannot convert, which from a DI
           constructor takes the application down with an error naming DI rather than the setting. */
        var chunker = new DocumentChunker(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RagSettings:ChunkTargetTokens"] = "six hundred",
                ["RagSettings:DirectIngestionMaxTokens"] = ""
            }).Build());

        Assert.Equal(650, chunker.TargetTokens);
        Assert.Equal(12000, chunker.DirectIngestionMaxTokens);
    }

    [Fact]
    public void AnOverlapWiderThanHalfAChunkIsClampedBack()
    {
        // An overlap at a full chunk's width would stop the walk advancing at all.
        var chunker = Chunker(target: 100, overlap: 500);

        Assert.Equal(50, chunker.OverlapTokens);
    }

    // ── Token estimation and the direct-ingestion threshold ─────────────────────

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    [InlineData("abcdefgh", 2)]
    public void EstimateTokensIsFourCharactersPerTokenRoundedUp(string? text, int expected)
        => Assert.Equal(expected, DocumentChunker.EstimateTokens(text));

    [Fact]
    public void ADocumentUnderTheThresholdGoesToTheModelWhole()
    {
        var chunker = Chunker(direct: 100);

        Assert.True(chunker.FitsDirectIngestion(new string('x', 8)));
        Assert.True(chunker.FitsDirectIngestion(new string('x', 396)));
    }

    [Fact]
    public void TheDirectIngestionBoundaryItselfStillFits()
    {
        // 100 tokens is 400 characters exactly, and the comparison is inclusive.
        var chunker = Chunker(direct: 100);

        Assert.True(chunker.FitsDirectIngestion(new string('x', 400)));
        Assert.False(chunker.FitsDirectIngestion(new string('x', 401)));
    }

    [Fact]
    public void ADocumentOverTheThresholdDoesNotFit()
    {
        var chunker = Chunker(direct: 100);

        Assert.False(chunker.FitsDirectIngestion(new string('x', 4000)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingAtAllFitsDirectIngestion(string? text)
    {
        // Zero tokens is under any threshold, and there is nothing to retrieve from either way.
        Assert.True(Chunker(direct: 100).FitsDirectIngestion(text));
    }

    // ── Chunking: what must never happen ───────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \r\n\t \r\n ")]
    public void EmptyAndWhitespaceOnlyInputYieldNoChunks(string? text)
        => Assert.Empty(Chunker(target: 40, overlap: 8).Chunk(text));

    [Fact]
    public void NoChunkIsEmptyOrNothingButWhitespace()
    {
        var chunks = Chunker(target: 40, overlap: 8).Chunk(Document() + "\n\n\n\n     ");

        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void TheChunksCoverEveryCharacterOfTheInput()
    {
        string document = Document();
        var chunks = Chunker(target: 40, overlap: 8).Chunk(document);

        Assert.NotEmpty(chunks);
        Assert.Equal(0, chunks[0].SourceOffset);
        Assert.Equal(document.Length, chunks[^1].SourceOffset + chunks[^1].Text.Length);

        /* Rebuilding the document out of the chunks is the strongest form of "nothing is lost":
           a dropped sentence, a gap between two chunks and an off-by-one offset all fail here. */
        var rebuilt = new StringBuilder();
        int covered = 0;
        foreach (var chunk in chunks)
        {
            Assert.Equal(chunk.Text, document.Substring(chunk.SourceOffset, chunk.Text.Length));
            Assert.True(chunk.SourceOffset <= covered, $"a gap opened before offset {chunk.SourceOffset}");

            int end = chunk.SourceOffset + chunk.Text.Length;
            if (end > covered)
            {
                rebuilt.Append(chunk.Text, covered - chunk.SourceOffset, end - covered);
                covered = end;
            }
        }

        Assert.Equal(document, rebuilt.ToString());
    }

    [Fact]
    public void OffsetsAndIndexesAdvanceTogether()
    {
        var chunks = Chunker(target: 40, overlap: 8).Chunk(Document());

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
            if (i > 0)
                Assert.True(chunks[i].SourceOffset > chunks[i - 1].SourceOffset, $"offset {i} did not advance");
        }
    }

    [Fact]
    public void EachChunkReportsItsOwnEstimatedTokens()
    {
        var chunks = Chunker(target: 40, overlap: 8).Chunk(Document());

        Assert.All(chunks, c => Assert.Equal(DocumentChunker.EstimateTokens(c.Text), c.TokenCount));
    }

    // ── Chunking: size and overlap ─────────────────────────────────────────────

    [Fact]
    public void ChunksSitInsideTheConfiguredSizeBand()
    {
        var chunks = Chunker(target: 40, overlap: 8).Chunk(Document());

        /* A 160-character target: no cut earlier than half of it, none later than 125% of it.
           The two extra characters are the blank line a chunk may open on, which is absorbed
           before the window is measured. The final chunk is exempt -- it is whatever is left. */
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            Assert.True(chunks[i].Text.Length >= 80, $"chunk {i} was {chunks[i].Text.Length} characters");
            Assert.True(chunks[i].Text.Length <= 202, $"chunk {i} was {chunks[i].Text.Length} characters");
        }
    }

    [Fact]
    public void ConsecutiveChunksOverlap()
    {
        /* A sentence sitting on a boundary has to appear whole in one chunk, or the retriever
           cannot match it in either. */
        var chunks = Chunker(target: 40, overlap: 8).Chunk(Document());

        Assert.True(chunks.Count > 2, $"only {chunks.Count} chunks");
        for (int i = 1; i < chunks.Count; i++)
        {
            int previousEnd = chunks[i - 1].SourceOffset + chunks[i - 1].Text.Length;
            Assert.True(chunks[i].SourceOffset < previousEnd, $"chunk {i} did not overlap chunk {i - 1}");
        }
    }

    [Fact]
    public void WithNoOverlapConfiguredTheChunksAbut()
    {
        var chunks = Chunker(target: 40, overlap: 0).Chunk(Document());

        for (int i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].SourceOffset + chunks[i - 1].Text.Length, chunks[i].SourceOffset);
        }
    }

    // ── Chunking: where the boundaries land ────────────────────────────────────

    [Fact]
    public void BoundariesLandOnStructureRatherThanInsideAWord()
    {
        /* A boundary through the middle of a word splits the very term a query would have
           matched and leaves both halves matching nothing. */
        string document = Document();
        var chunks = Chunker(target: 40, overlap: 8).Chunk(document);

        for (int i = 0; i < chunks.Count - 1; i++)
        {
            int end = chunks[i].SourceOffset + chunks[i].Text.Length;
            char last = document[end - 1];

            Assert.True(char.IsWhiteSpace(last) || last is '.' or '!' or '?',
                $"chunk {i} ended mid-word: ...{document.Substring(Math.Max(0, end - 14), Math.Min(14, end))}");
        }
    }

    [Fact]
    public void AParagraphBreakInsideTheWindowBeatsAPlainSpace()
    {
        // A 20-token target is 80 characters, so the first boundary is looked for in 40..100.
        var chunker = Chunker(target: 20, overlap: 0);

        // Four twelve-letter words and their spaces: 52 characters, no sentence punctuation.
        string opening = string.Concat(Enumerable.Range(0, 4).Select(i => new string((char)('a' + i), 12) + " "));
        string document = opening + "\n\n" + new string('e', 300);

        var chunks = chunker.Chunk(document);

        // The blank line at 52 wins over every space in the window, and the run is taken whole.
        Assert.Equal(opening + "\n\n", chunks[0].Text);
    }

    [Fact]
    public void ASentenceEndInsideTheWindowBeatsAPlainSpace()
    {
        var chunker = Chunker(target: 20, overlap: 0);

        // 20 letters, a space, 20 letters, a full stop at index 41, a space at 42.
        string opening = new string('a', 20) + " " + new string('b', 20) + ". ";
        string document = opening + new string('c', 5) + " " + new string('d', 300);

        var chunks = chunker.Chunk(document);

        // The cut is immediately after the full stop, not at the later space a plain
        // whitespace search would have preferred.
        Assert.Equal(42, chunks[0].Text.Length);
        Assert.EndsWith(".", chunks[0].Text);
    }

    [Fact]
    public void AWordBreakIsUsedWhenThereIsNoSentenceOrParagraphBoundary()
    {
        var chunker = Chunker(target: 20, overlap: 0);

        // Ten-letter words and single spaces, so the only boundaries available are spaces.
        string document = string.Concat(Enumerable.Repeat("abcdefghij ", 40));

        var chunks = chunker.Chunk(document);

        Assert.True(chunks.Count > 2, $"only {chunks.Count} chunks");
        for (int i = 0; i < chunks.Count - 1; i++)
            Assert.EndsWith(" ", chunks[i].Text);
    }

    [Fact]
    public void AnUnbrokenHundredKilobyteWordStillChunks()
    {
        /* No boundary of any kind exists here, so every cut is mid-word -- the last resort. It
           must still terminate promptly and still respect the size band, rather than hanging or
           handing back one enormous chunk. */
        string document = new string('z', 100_000);

        var chunks = Chunker(target: 650, overlap: 80).Chunk(document);

        Assert.True(chunks.Count > 20, $"only {chunks.Count} chunks");
        // 2,600-character target plus the 25% tolerance.
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 3250, $"a chunk was {c.Text.Length} characters"));
        Assert.Equal(0, chunks[0].SourceOffset);
        Assert.Equal(document.Length, chunks[^1].SourceOffset + chunks[^1].Text.Length);
    }

    // ── Determinism ────────────────────────────────────────────────────────────

    [Fact]
    public void ChunkingTheSameDocumentTwiceGivesTheSameChunks()
    {
        /* The caller stores chunk text and its embeddings for a later turn, so a chunker that
           drifted between runs would silently pair one turn's text with another turn's vectors. */
        string document = Document();
        var chunker = Chunker(target: 40, overlap: 8);

        Assert.Equal(chunker.Chunk(document), chunker.Chunk(document));
    }

    [Fact]
    public void TwoChunkersWithTheSameSettingsAgree()
    {
        string document = Document();

        Assert.Equal(
            Chunker(target: 40, overlap: 8).Chunk(document),
            Chunker(target: 40, overlap: 8).Chunk(document));
    }
}
