using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Overseer.Services.Rag;

/// <summary>A chunk chosen for the prompt, with why it was chosen.</summary>
public sealed record RetrievedChunk
{
    public DocumentChunk Chunk { get; init; } = new();

    public double Score { get; init; }

    /// <summary><c>embedding</c> or <c>bm25</c> — which retriever chose it.</summary>
    public string Method { get; init; } = "";
}

/// <summary>What retrieval did, in terms the user can be shown.</summary>
public sealed record RagResult
{
    /// <summary>The chunks to put in the prompt, in document order (not score order).</summary>
    public IReadOnlyList<RetrievedChunk> Chunks { get; init; } = Array.Empty<RetrievedChunk>();

    public int TotalChunks { get; init; }

    /// <summary>Fraction of the document's characters actually sent, 0..1.</summary>
    public double CoverageFraction { get; init; }

    /// <summary><c>embedding</c> or <c>bm25</c>.</summary>
    public string Method { get; init; } = "";
}

/// <summary>
/// Picks the passages of a large document that answer a particular query, by vector similarity
/// where a local model is available and by BM25 where it is not.
/// </summary>
/// <remarks>
/// <para>
/// **BM25 is the shipping path, not a stub.** The embedding model is a large binary that is not
/// carried in the repository, so on a fresh checkout, in CI, and on any deployment that has not
/// downloaded it, BM25 is what actually runs. It is therefore a real BM25 rather than a
/// term-overlap count.
/// </para>
/// <para>
/// **This service holds no state and writes no file.** Chunk text is verbatim document content
/// and an embedding is invertible enough to be treated the same way, so where those may be stored
/// — encrypted for a confidential session, not at all for an ephemeral one — is the caller's
/// decision, not this class's.
/// </para>
/// </remarks>
public sealed class DocumentRagService
{
    /// <summary>Ranked by vector similarity against a local embedding model.</summary>
    public const string MethodEmbedding = "embedding";

    /// <summary>Ranked by BM25 over the document's own chunks.</summary>
    public const string MethodBm25 = "bm25";

    /// <summary>Not ranked at all: the opening chunks, after a failure.</summary>
    public const string MethodHead = "head";

    /* BM25's term-frequency saturation. At 1.2 the fifth occurrence of a term is worth much less
       than the second, so a chunk cannot win by repeating a word. */
    private const double K1 = 1.2;

    /* Length normalisation. At b = 0 a long chunk wins simply by holding more words; at b = 1 the
       score is divided fully by length, which over-rewards a very short one. 0.75 is the usual
       compromise, and it earns its place here because these chunks are not uniform -- the last
       chunk of a document is routinely a fraction of the size of the others, and a paragraph
       break can end one well short of the target. */
    private const double B = 0.75;

    /* Deliberately short. A longer list starts discarding words that carry meaning in a technical
       document -- "not", "no", "off" and "all" among them -- and the whole benefit here is
       stopping "the" from deciding a ranking. */
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "been", "but", "by", "for", "from", "had",
        "has", "have", "he", "her", "his", "i", "in", "is", "it", "its", "of", "on", "or", "she",
        "that", "the", "their", "them", "there", "these", "they", "this", "to", "was", "were",
        "will", "with", "you", "your"
    };

    private readonly DocumentChunker _chunker;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<DocumentRagService>? _logger;

    public DocumentRagService(
        DocumentChunker chunker,
        IEmbeddingService embeddings,
        IConfiguration configuration,
        ILogger<DocumentRagService>? logger = null)
    {
        _chunker = chunker;
        _embeddings = embeddings;
        _logger = logger;

        MaxRetrievedChunks = Math.Clamp(
            RagConfig.ReadInt(configuration.GetSection("RagSettings"), "MaxRetrievedChunks", 6), 1, 200);
    }

    /// <summary><c>RagSettings:MaxRetrievedChunks</c>, default 6.</summary>
    public int MaxRetrievedChunks { get; }

    /// <summary>
    /// Chunks the text, embeds it (or falls back to BM25), and returns the best chunks for this
    /// query. Never throws: on any failure it returns the first <see cref="MaxRetrievedChunks"/>
    /// chunks with method <c>head</c>, so an answer is still grounded in something rather than
    /// nothing.
    /// </summary>
    public async Task<RagResult> RetrieveAsync(string documentText, string query, CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentChunk> chunks = Array.Empty<DocumentChunk>();

        try
        {
            chunks = _chunker.Chunk(documentText);
            if (chunks.Count == 0)
                return new RagResult { Method = MethodBm25 };

            IReadOnlyList<float[]> vectors = Array.Empty<float[]>();

            if (_embeddings.IsAvailable)
            {
                try
                {
                    vectors = await _embeddings
                        .EmbedAsync(chunks.Select(c => c.Text).ToList(), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogWarning(ex,
                        "The '{Engine}' embedding engine failed over {Count} chunks; ranking with BM25 instead.",
                        _embeddings.Name, chunks.Count);
                }

                if (vectors.Count != 0 && vectors.Count != chunks.Count)
                {
                    _logger?.LogWarning(
                        "The '{Engine}' embedding engine returned {Returned} vectors for {Count} chunks; ranking with BM25 instead.",
                        _embeddings.Name, vectors.Count, chunks.Count);
                    vectors = Array.Empty<float[]>();
                }
            }

            return await RetrieveFromAsync(chunks, vectors, query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex,
                "Retrieval failed; falling back to the first {Max} chunks of the document.", MaxRetrievedChunks);
            return Head(chunks);
        }
    }

    /// <summary>
    /// Retrieval over chunks whose embeddings the caller already has, for a second turn on the
    /// same document. Embeddings may be empty, in which case BM25 is used.
    /// </summary>
    public async Task<RagResult> RetrieveFromAsync(
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<float[]> embeddings,
        string query,
        CancellationToken cancellationToken)
    {
        if (chunks == null || chunks.Count == 0)
            return new RagResult { Method = MethodBm25 };

        try
        {
            if (AreUsable(chunks, embeddings))
            {
                var byVector = await TryRankByVectorAsync(chunks, embeddings, query, cancellationToken)
                    .ConfigureAwait(false);
                if (byVector != null)
                    return byVector;
            }

            return BuildResult(chunks, Bm25Scores(chunks, query), MethodBm25);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex,
                "Retrieval over {Count} chunks failed; falling back to the first {Max} of them.",
                chunks.Count, MaxRetrievedChunks);
            return Head(chunks);
        }
    }

    /// <summary>
    /// Whether the caller's vectors can rank these chunks at all: one vector per chunk, none
    /// empty, all the same width. Anything else means BM25 rather than a wrong ranking.
    /// </summary>
    private static bool AreUsable(IReadOnlyList<DocumentChunk> chunks, IReadOnlyList<float[]>? embeddings)
    {
        if (embeddings == null || embeddings.Count != chunks.Count)
            return false;

        int width = embeddings[0] == null ? 0 : embeddings[0].Length;
        if (width == 0)
            return false;

        for (int i = 0; i < embeddings.Count; i++)
        {
            if (embeddings[i] == null || embeddings[i].Length != width)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Ranks by cosine similarity against the query's own embedding. Returns null when the query
    /// cannot be embedded, which means BM25 rather than a failed turn.
    /// </summary>
    private async Task<RagResult?> TryRankByVectorAsync(
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<float[]> embeddings,
        string query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<float[]> queryVectors;
        try
        {
            queryVectors = await _embeddings
                .EmbedAsync(new[] { query ?? string.Empty }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Embedding the query failed; ranking with BM25 instead.");
            return null;
        }

        if (queryVectors.Count == 0)
            return null;

        float[] queryVector = queryVectors[0];

        /* A query embedded by a different model than the chunks would score nonsense with perfect
           confidence, so a width mismatch is a fallback rather than something to work around. */
        if (queryVector == null || queryVector.Length != embeddings[0].Length)
        {
            _logger?.LogWarning(
                "The query embedding is {QueryWidth} wide but the chunk embeddings are {ChunkWidth}; ranking with BM25 instead.",
                queryVector?.Length ?? 0, embeddings[0].Length);
            return null;
        }

        var scores = new double[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
            scores[i] = Cosine(embeddings[i], queryVector);

        return BuildResult(chunks, scores, MethodEmbedding);
    }

    /// <summary>
    /// Similarity between two vectors the embedding contract promises are L2-normalised, which
    /// makes the plain dot product the cosine — no per-comparison square roots.
    /// </summary>
    private static double Cosine(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += (double)a[i] * b[i];

        return sum;
    }

    /// <summary>Okapi BM25 over the chunk set, with the query's non-stop-word terms.</summary>
    /// <remarks>
    /// The corpus here is <em>one document's own chunks</em>, not a collection of documents, so
    /// IDF is much weaker than a library BM25 would enjoy: a term in every chunk of a ten-chunk
    /// document earns almost no discount, and a rare term earns far less lift. The ranking
    /// therefore leans on term frequency and length normalisation more than it otherwise would.
    /// That is expected at this corpus size rather than a defect — the job is to order one
    /// document's passages against each other, and the terms that distinguish them are exactly
    /// the ones IDF still separates.
    /// </remarks>
    private static double[] Bm25Scores(IReadOnlyList<DocumentChunk> chunks, string query)
    {
        var scores = new double[chunks.Count];

        var queryTerms = Tokenize(query).Distinct(StringComparer.Ordinal).ToList();
        if (queryTerms.Count == 0)
            return scores;

        var frequencies = new Dictionary<string, int>[chunks.Count];
        var lengths = new int[chunks.Count];

        for (int i = 0; i < chunks.Count; i++)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string term in Tokenize(chunks[i].Text))
            {
                counts.TryGetValue(term, out int seen);
                counts[term] = seen + 1;
                lengths[i]++;
            }

            frequencies[i] = counts;
        }

        double averageLength = lengths.Average();
        if (averageLength <= 0)
            return scores;

        foreach (string term in queryTerms)
        {
            int documentFrequency = frequencies.Count(f => f.ContainsKey(term));
            if (documentFrequency == 0)
                continue;

            /* The non-negative IDF variant. Textbook BM25's IDF goes negative for a term present
               in more than half the corpus, which over one document's chunks would actively
               penalise the very word the user asked about. */
            double idf = Math.Log(1 + (chunks.Count - documentFrequency + 0.5) / (documentFrequency + 0.5));

            for (int i = 0; i < chunks.Count; i++)
            {
                if (!frequencies[i].TryGetValue(term, out int termFrequency))
                    continue;

                double normalisation = K1 * (1 - B + B * lengths[i] / averageLength);
                scores[i] += idf * (termFrequency * (K1 + 1)) / (termFrequency + normalisation);
            }
        }

        return scores;
    }

    /// <summary>Lowercased runs of letters and digits, minus the stop words.</summary>
    private static List<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text))
            return tokens;

        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool inTerm = i < text.Length && char.IsLetterOrDigit(text[i]);

            if (inTerm)
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                string token = text[start..i].ToLowerInvariant();
                if (!StopWords.Contains(token))
                    tokens.Add(token);

                start = -1;
            }
        }

        return tokens;
    }

    /// <summary>Takes the highest-scoring chunks and hands them back in document order.</summary>
    private RagResult BuildResult(IReadOnlyList<DocumentChunk> chunks, double[] scores, string method)
    {
        var selected = chunks
            .Select((chunk, i) => new RetrievedChunk { Chunk = chunk, Score = scores[i], Method = method })
            // Index breaks score ties, so the same document and query always select the same chunks.
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Chunk.Index)
            .Take(MaxRetrievedChunks)
            /* Document order, not score order. A model handed excerpts in score order narrates
               them in score order, and the reader cannot tell a jump in the document from a jump
               in the argument. */
            .OrderBy(r => r.Chunk.Index)
            .ToList();

        return new RagResult
        {
            Chunks = selected,
            TotalChunks = chunks.Count,
            CoverageFraction = Coverage(chunks, selected),
            Method = method
        };
    }

    /// <summary>
    /// The opening chunks, unranked. The floor under every failure: an answer grounded in the
    /// start of the document beats an answer grounded in nothing, and the method name says
    /// plainly that no ranking happened, so the caller can tell the user as much.
    /// </summary>
    private RagResult Head(IReadOnlyList<DocumentChunk> chunks)
    {
        var selected = chunks
            .Take(MaxRetrievedChunks)
            .Select(c => new RetrievedChunk { Chunk = c, Score = 0, Method = MethodHead })
            .ToList();

        return new RagResult
        {
            Chunks = selected,
            TotalChunks = chunks.Count,
            CoverageFraction = Coverage(chunks, selected),
            Method = MethodHead
        };
    }

    /// <summary>The share of the document actually sent, measured in characters.</summary>
    /// <remarks>
    /// The denominator is the characters across all chunks rather than the source document's own
    /// length. Chunks overlap, so the two differ a little — but this way the figure means the same
    /// thing from both entry points, one of which never sees the source text, and it reads exactly
    /// 1.0 when every chunk was selected.
    /// </remarks>
    private static double Coverage(IReadOnlyList<DocumentChunk> all, IReadOnlyList<RetrievedChunk> selected)
    {
        long total = all.Sum(c => (long)c.Text.Length);
        if (total <= 0)
            return 0;

        long sent = selected.Sum(r => (long)r.Chunk.Text.Length);
        return Math.Clamp((double)sent / total, 0, 1);
    }
}
