using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Rag;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class DocumentRagServiceTests
{
    // ── Test doubles for the embedding layer ───────────────────────────────────

    /// <summary>
    /// No model at all, which is the state of every machine that has not downloaded the ONNX
    /// file — including CI. This is the path that actually ships today.
    /// </summary>
    private sealed class NoEmbeddings : IEmbeddingService
    {
        public string Name => "none";

        public bool IsAvailable => false;

        public int Dimensions => 0;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<float[]>>(Array.Empty<float[]>());
    }

    /// <summary>
    /// Deterministic vectors: one axis per keyword plus a constant axis, L2-normalised so a dot
    /// product is the cosine — exactly what the real contract promises.
    /// </summary>
    /// <remarks>
    /// The constant axis keeps a text that mentions none of the keywords off the zero vector, so
    /// an unrelated chunk still has a defined, and lower, similarity.
    /// </remarks>
    private sealed class KeywordEmbeddings : IEmbeddingService
    {
        private static readonly string[] Axes = { "temple", "kitchen", "harbour" };

        public string Name => "keyword-fake";

        public bool IsAvailable => true;

        public int Dimensions => Axes.Length + 1;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToList());

        private static float[] Vector(string text)
        {
            var vector = new float[Axes.Length + 1];
            for (int i = 0; i < Axes.Length; i++)
                vector[i] = Occurrences(text, Axes[i]);

            vector[Axes.Length] = 1;

            double norm = Math.Sqrt(vector.Sum(v => (double)v * v));
            for (int i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] / norm);

            return vector;
        }

        private static int Occurrences(string text, string term)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                at += term.Length;
            }

            return count;
        }
    }

    /// <summary>Reports itself available, then fails when actually asked to embed.</summary>
    private sealed class FailingEmbeddings : IEmbeddingService
    {
        public string Name => "failing";

        public bool IsAvailable => true;

        public int Dimensions => 4;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => throw new InvalidOperationException("inference failed");
    }

    /// <summary>Broken rather than merely absent: even asking whether it works throws.</summary>
    private sealed class BrokenEmbeddings : IEmbeddingService
    {
        public string Name => "broken";

        public bool IsAvailable => throw new InvalidOperationException("the engine is broken");

        public int Dimensions => 0;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => throw new InvalidOperationException("the engine is broken");
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────

    private static DocumentRagService Service(
        IEmbeddingService embeddings, int max = 6, int target = 40, int overlap = 8)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RagSettings:MaxRetrievedChunks"] = max.ToString(CultureInfo.InvariantCulture),
            ["RagSettings:ChunkTargetTokens"] = target.ToString(CultureInfo.InvariantCulture),
            ["RagSettings:ChunkOverlapTokens"] = overlap.ToString(CultureInfo.InvariantCulture)
        }).Build();

        return new DocumentRagService(new DocumentChunker(configuration), embeddings, configuration);
    }

    /// <summary>Chunks laid out end to end, so the offsets are what a real chunker would give.</summary>
    private static IReadOnlyList<DocumentChunk> Chunks(params string[] texts)
    {
        var chunks = new List<DocumentChunk>();
        int offset = 0;

        for (int i = 0; i < texts.Length; i++)
        {
            chunks.Add(new DocumentChunk
            {
                Index = i,
                Text = texts[i],
                TokenCount = DocumentChunker.EstimateTokens(texts[i]),
                SourceOffset = offset
            });

            offset += texts[i].Length;
        }

        return chunks;
    }

    /// <summary>A document long enough to need chunking, with the query's subject in two places.</summary>
    private static string Document()
    {
        string[] topics =
        {
            "kitchen inventory", "harbour tides", "temple wardens",
            "market prices", "stable repairs", "temple sanctum"
        };

        var text = new StringBuilder();
        for (int p = 0; p < topics.Length; p++)
        {
            for (int s = 0; s < 6; s++)
                text.Append($"Section {p} note {s} concerning {topics[p]} and the daily work it needs. ");

            text.Append("\n\n");
        }

        return text.ToString();
    }

    // ── Configuration ──────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultRetrievalWidthIsSix()
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new DocumentRagService(new DocumentChunker(configuration), new NoEmbeddings(), configuration);

        Assert.Equal(6, service.MaxRetrievedChunks);
    }

    // ── BM25 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bm25RanksAnObviouslyRelevantChunkAboveAnIrrelevantOne()
    {
        var chunks = Chunks(
            "The kitchen inventory lists flour, salt and three copper pans.",
            "The wardens guard the inner sanctum of the ancient temple.",
            "Harbour tides run strongest at dusk, so the ferry leaves early.");

        var result = await Service(new NoEmbeddings(), max: 1).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "who guards the temple sanctum",
            TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodBm25, result.Method);
        Assert.Equal(1, Assert.Single(result.Chunks).Chunk.Index);
    }

    [Fact]
    public async Task AQueryWithNoMatchingTermStillReturnsChunks()
    {
        /* Zero scores across the board must not read as "no context": an answer grounded in the
           opening of the document beats an answer grounded in nothing. */
        var chunks = Chunks("alpha beta gamma", "delta epsilon zeta", "eta theta iota");

        var result = await Service(new NoEmbeddings(), max: 2).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "xyzzy plugh", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal(DocumentRagService.MethodBm25, result.Method);
        Assert.All(result.Chunks, c => Assert.Equal(0d, c.Score));
    }

    [Fact]
    public async Task AQueryOfNothingButStopWordsStillReturnsChunks()
    {
        var chunks = Chunks("alpha beta gamma", "delta epsilon zeta");

        var result = await Service(new NoEmbeddings(), max: 1).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "the of and to", TestContext.Current.CancellationToken);

        Assert.Equal(0, Assert.Single(result.Chunks).Chunk.Index);
    }

    [Fact]
    public async Task StopWordsDoNotDecideTheRanking()
    {
        /* Without a stop-word list the first chunk wins on the sheer number of "the"s while
           saying nothing at all about what was asked. */
        var chunks = Chunks(
            "The the the the of and to the in a the of and to the the in the of.",
            "Wardens of the temple keep the sanctum sealed.");

        var result = await Service(new NoEmbeddings(), max: 1).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "the temple", TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(result.Chunks).Chunk.Index);
    }

    [Fact]
    public async Task ALongChunkDoesNotOutrankAShortOneMerelyBySize()
    {
        /* What b = 0.75 buys: the padding chunk mentions the term once in a great many words,
           the short one is about it. Without length normalisation the long chunk wins. */
        var chunks = Chunks(
            "The temple " + string.Concat(Enumerable.Repeat("assorted unrelated municipal filing notes ", 40)),
            "Temple temple temple.");

        var result = await Service(new NoEmbeddings(), max: 1).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "temple", TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(result.Chunks).Chunk.Index);
    }

    // ── Selection, ordering and coverage ───────────────────────────────────────

    [Fact]
    public async Task RetrievalReturnsAtMostTheConfiguredNumberOfChunksInDocumentOrder()
    {
        var chunks = Chunks(
            "temple temple temple temple",
            "harbour tides and ferries",
            "temple wardens",
            "flour salt copper pans",
            "the temple sanctum temple");

        var result = await Service(new NoEmbeddings(), max: 3).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "temple", TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Chunks.Count);
        Assert.Equal(5, result.TotalChunks);

        /* Document order, not score order: a model handed excerpts in score order narrates them
           in score order, and the reader cannot tell a jump in the document from a jump in the
           argument. */
        var indexes = result.Chunks.Select(c => c.Chunk.Index).ToList();
        Assert.Equal(new[] { 0, 2, 4 }, indexes);
    }

    [Fact]
    public async Task CoverageIsTheShareOfTheDocumentActuallySent()
    {
        var chunks = Chunks(
            new string('a', 100), new string('b', 100), new string('c', 100), new string('d', 100));

        var result = await Service(new NoEmbeddings(), max: 2).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "nothing matches this", TestContext.Current.CancellationToken);

        // Two of four equally sized chunks: half of the characters reach the model.
        Assert.Equal(0.5, result.CoverageFraction, 6);
    }

    [Fact]
    public async Task CoverageIsOneWhenEveryChunkIsSent()
    {
        var chunks = Chunks("alpha", "beta", "gamma");

        var result = await Service(new NoEmbeddings(), max: 6).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "alpha", TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Chunks.Count);
        Assert.Equal(1.0, result.CoverageFraction, 6);
    }

    [Fact]
    public async Task AnEmptyDocumentRetrievesNothingRatherThanThrowing()
    {
        var result = await Service(new NoEmbeddings()).RetrieveAsync(
            "   \r\n  ", "temple", TestContext.Current.CancellationToken);

        Assert.Empty(result.Chunks);
        Assert.Equal(0, result.TotalChunks);
        Assert.Equal(0d, result.CoverageFraction);
    }

    // ── The path that ships: no embedding model ────────────────────────────────

    [Fact]
    public async Task WithNoEmbeddingModelRetrievalStillWorksAndSaysItUsedBm25()
    {
        /* The ONNX model is a large binary that is not carried in the repository, so BM25 is
           what actually runs on a fresh checkout, in CI, and anywhere the file was not
           downloaded. */
        var result = await Service(new NoEmbeddings(), max: 2).RetrieveAsync(
            Document(), "who guards the temple sanctum", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodBm25, result.Method);
        Assert.Equal(2, result.Chunks.Count);
        Assert.All(result.Chunks, c => Assert.Equal(DocumentRagService.MethodBm25, c.Method));
        Assert.True(result.TotalChunks > 2, $"only {result.TotalChunks} chunks");
        Assert.True(result.CoverageFraction is > 0 and < 1, $"coverage was {result.CoverageFraction}");
    }

    [Fact]
    public async Task RetrieveFromWithoutEmbeddingsFallsBackToBm25()
    {
        /* The second-turn path when nothing was stored -- an ephemeral session writes no
           sidecar, so its chunks come back without vectors. */
        var chunks = Chunks("temple wardens", "kitchen pans");

        var result = await Service(new KeywordEmbeddings(), max: 1).RetrieveFromAsync(
            chunks, Array.Empty<float[]>(), "temple", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodBm25, result.Method);
        Assert.Equal(0, Assert.Single(result.Chunks).Chunk.Index);
    }

    // ── The embedding path ─────────────────────────────────────────────────────

    [Fact]
    public async Task WithVectorsTheNearestChunkIsChosenAndTheMethodSaysEmbedding()
    {
        var chunks = Chunks(
            "flour salt and three copper pans",
            "the temple temple temple wardens",
            "harbour tides run strongest at dusk");

        var embeddings = new KeywordEmbeddings();
        var vectors = await embeddings.EmbedAsync(
            chunks.Select(c => c.Text).ToList(), TestContext.Current.CancellationToken);

        var result = await Service(embeddings, max: 1).RetrieveFromAsync(
            chunks, vectors, "temple", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodEmbedding, result.Method);
        Assert.Equal(1, Assert.Single(result.Chunks).Chunk.Index);

        // Both vectors are unit length, so the score is a cosine and 1.0 is its ceiling.
        Assert.True(result.Chunks[0].Score is > 0 and <= 1.000001, $"score was {result.Chunks[0].Score}");
    }

    [Fact]
    public async Task RetrieveAsyncUsesTheEmbeddingServiceWhenOneIsAvailable()
    {
        var result = await Service(new KeywordEmbeddings(), max: 2).RetrieveAsync(
            Document(), "temple sanctum", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodEmbedding, result.Method);
        Assert.Equal(2, result.Chunks.Count);
        Assert.All(result.Chunks, c => Assert.Equal(DocumentRagService.MethodEmbedding, c.Method));
    }

    [Fact]
    public async Task VectorsThatDoNotMatchTheChunksAreIgnoredRatherThanTrusted()
    {
        // A wrong-length vector set would score nonsense with perfect confidence.
        var chunks = Chunks("temple wardens", "kitchen pans", "harbour tides");

        var result = await Service(new KeywordEmbeddings(), max: 1).RetrieveFromAsync(
            chunks, new[] { new float[] { 1, 0, 0, 0 } }, "temple", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodBm25, result.Method);
    }

    [Fact]
    public async Task AnEmbeddingServiceThatFailsDegradesToBm25RatherThanFailingTheTurn()
    {
        var result = await Service(new FailingEmbeddings(), max: 2).RetrieveAsync(
            Document(), "temple", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodBm25, result.Method);
        Assert.Equal(2, result.Chunks.Count);
    }

    [Fact]
    public async Task AnUnexpectedFailureFallsBackToTheHeadOfTheDocument()
    {
        /* The floor under everything: whatever breaks, the model is still given the opening of
           the document rather than nothing, and the method name says no ranking happened. */
        var result = await Service(new BrokenEmbeddings(), max: 3).RetrieveAsync(
            Document(), "temple", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodHead, result.Method);
        Assert.Equal(new[] { 0, 1, 2 }, result.Chunks.Select(c => c.Chunk.Index));
        Assert.All(result.Chunks, c => Assert.Equal(DocumentRagService.MethodHead, c.Method));
    }

    // ── Determinism ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSameQueryTwiceGivesTheSameRanking()
    {
        var service = Service(new NoEmbeddings(), max: 3);
        string document = Document();

        var first = await service.RetrieveAsync(document, "temple sanctum", TestContext.Current.CancellationToken);
        var second = await service.RetrieveAsync(document, "temple sanctum", TestContext.Current.CancellationToken);

        Assert.Equal(first.Chunks, second.Chunks);
        Assert.Equal(first.CoverageFraction, second.CoverageFraction, 12);
        Assert.Equal(first.TotalChunks, second.TotalChunks);
    }

    // ── The local ONNX engine, which normally has no model to load ─────────────

    [Fact]
    public void AnUnconfiguredEmbeddingModelIsNotAFailure()
    {
        /* No model configured is the ordinary state of a development machine. Throwing from the
           constructor of a DI singleton would take down an application whose other functionality
           is entirely fine. */
        using var service = new LocalOnnxEmbeddingService(new ConfigurationBuilder().Build());

        Assert.False(service.IsAvailable);
        Assert.Equal(0, service.Dimensions);
        Assert.Equal("none", service.Name);
    }

    [Fact]
    public void AMissingEmbeddingModelFileIsAWarningRatherThanAFailure()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RagSettings:EmbeddingModelPath"] = Path.Combine(Path.GetTempPath(), "overseer-no-such-model.onnx"),
            ["RagSettings:EmbeddingVocabPath"] = Path.Combine(Path.GetTempPath(), "overseer-no-such-vocab.txt")
        }).Build();

        using var service = new LocalOnnxEmbeddingService(configuration);

        Assert.False(service.IsAvailable);
        Assert.Equal(0, service.Dimensions);
        Assert.Equal("none", service.Name);
    }

    [Fact]
    public async Task AnUnavailableEmbeddingServiceReturnsNoVectorsRatherThanThrowing()
    {
        using var service = new LocalOnnxEmbeddingService(new ConfigurationBuilder().Build());

        var vectors = await service.EmbedAsync(new[] { "anything at all" }, TestContext.Current.CancellationToken);

        Assert.Empty(vectors);
    }

    [Fact]
    public async Task AModelFileThatIsNotAModelIsRejectedAtConstruction()
    {
        /* Discovered here rather than on a user's first upload, which is the worst possible
           moment to find out the file on disk is not a model. */
        string directory = Path.Combine(Path.GetTempPath(), "overseer-rag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string model = Path.Combine(directory, "model.onnx");
            string vocab = Path.Combine(directory, "vocab.txt");
            await File.WriteAllTextAsync(model, "this is not a protobuf", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(vocab, "[PAD]\n[UNK]\n[CLS]\n[SEP]\n[MASK]\nhello\nworld\n",
                TestContext.Current.CancellationToken);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RagSettings:EmbeddingModelPath"] = model,
                ["RagSettings:EmbeddingVocabPath"] = vocab
            }).Build();

            using var service = new LocalOnnxEmbeddingService(configuration);

            Assert.False(service.IsAvailable);
            Assert.Equal(0, service.Dimensions);
            Assert.Empty(await service.EmbedAsync(new[] { "hello world" }, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RetrievalOverAnUnloadedLocalEngineUsesBm25()
    {
        /* The two halves joined up: the real engine with no model, driving the real service. This
           is precisely the configuration that runs in production until the model is deployed. */
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RagSettings:MaxRetrievedChunks"] = "2",
            ["RagSettings:ChunkTargetTokens"] = "40"
        }).Build();

        using var embeddings = new LocalOnnxEmbeddingService(configuration);
        var service = new DocumentRagService(new DocumentChunker(configuration), embeddings, configuration);

        var result = await service.RetrieveAsync(
            Document(), "temple sanctum wardens", TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRagService.MethodBm25, result.Method);
        Assert.Equal(2, result.Chunks.Count);
    }
}
