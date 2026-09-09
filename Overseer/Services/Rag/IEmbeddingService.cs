using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Rag;

/// <summary>Turns text into vectors for similarity search.</summary>
/// <remarks>
/// Every member is answerable without a model present, because the model normally is not: the
/// contract's whole shape exists so a caller can discover that embeddings are unavailable and
/// rank with BM25 instead, rather than handling an exception on a user's first upload.
/// </remarks>
public interface IEmbeddingService
{
    /// <summary>
    /// Engine name for logs and for the provenance notice, for example <c>all-MiniLM-L6-v2</c>
    /// or <c>none</c>.
    /// </summary>
    string Name { get; }

    /// <summary>False when no model is loaded; the caller falls back to BM25.</summary>
    bool IsAvailable { get; }

    /// <summary>Vector length, or 0 when unavailable.</summary>
    int Dimensions { get; }

    /// <summary>
    /// One vector per input, L2-normalised so a dot product is the cosine. Returns an empty list
    /// when unavailable rather than throwing.
    /// </summary>
    /// <remarks>
    /// Normalisation is part of the contract rather than the caller's business: an implementation
    /// that returned raw vectors would make every ranking silently wrong, because the scorer
    /// treats the dot product as a cosine and cannot tell the difference.
    /// </remarks>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
