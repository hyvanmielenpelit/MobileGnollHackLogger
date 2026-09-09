using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Overseer.Services.Rag;

/// <summary>
/// Sentence embeddings computed inside this process with ONNX Runtime.
/// </summary>
/// <remarks>
/// <para>
/// **Zero network egress.** The model and its vocabulary are read from local paths and inference
/// runs in-process; there is no HTTP client here and nothing that could acquire one. That is the
/// property that lets a confidential document be embedded at all — an embedding is derived from
/// the text closely enough that it has to be treated as the text.
/// </para>
/// <para>
/// **A missing model is a warning, not a failure.** The model is a large binary that is not
/// carried in the repository, so an ordinary checkout and every CI run have no model at all. The
/// constructor therefore never throws: it reports the problem once, leaves
/// <see cref="IsAvailable"/> false, and retrieval ranks with BM25 instead. There is precedent in
/// <c>ConfigurationContentKeyRing</c>, for the same reason — throwing from the constructor of a
/// DI singleton takes down an application whose other functionality is entirely fine, and
/// deferring the throw surfaces the misconfiguration on a user's first upload, which is the worst
/// moment to discover it.
/// </para>
/// </remarks>
public sealed class LocalOnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private const string InputIdsName = "input_ids";
    private const string AttentionMaskName = "attention_mask";
    private const string TokenTypeIdsName = "token_type_ids";

    /* BERT's [PAD] id. Padding is masked out of the mean anyway, so the value only has to be
       inside the vocabulary. */
    private const long PadTokenId = 0;

    private readonly ILogger<LocalOnnxEmbeddingService>? _logger;
    private readonly int _batchSize;
    private readonly int _maxTokens;

    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private string? _outputName;
    private bool _wantsAttentionMask;
    private bool _wantsTokenTypeIds;

    public LocalOnnxEmbeddingService(IConfiguration configuration, ILogger<LocalOnnxEmbeddingService>? logger = null)
    {
        _logger = logger;
        Name = "none";

        var section = configuration.GetSection("RagSettings");
        _batchSize = Math.Clamp(RagConfig.ReadInt(section, "EmbeddingBatchSize", 16), 1, 256);
        _maxTokens = Math.Clamp(RagConfig.ReadInt(section, "EmbeddingMaxTokens", 256), 16, 8192);

        string? modelPath = section["EmbeddingModelPath"]?.Trim();
        string? vocabPath = section["EmbeddingVocabPath"]?.Trim();

        if (string.IsNullOrEmpty(modelPath) || string.IsNullOrEmpty(vocabPath))
        {
            /* Information rather than a warning: no model configured is the ordinary state of a
               development machine, not a mistake to chase. */
            _logger?.LogInformation(
                "No local embedding model is configured (RagSettings:EmbeddingModelPath and :EmbeddingVocabPath); document retrieval will rank with BM25.");
            return;
        }

        if (!File.Exists(modelPath) || !File.Exists(vocabPath))
        {
            _logger?.LogWarning(
                "The local embedding model is configured but missing: model '{ModelPath}', vocabulary '{VocabPath}'. Document retrieval will rank with BM25.",
                modelPath, vocabPath);
            return;
        }

        try
        {
            _tokenizer = BertTokenizer.Create(vocabPath, null);

            /* One intra-op thread on purpose. This runs inside a request-scoped turn alongside
               prompt assembly, tool calls and whatever else the server is serving; a math library
               that helps itself to every core makes the whole process stutter for the sake of one
               user's upload. */
            /* Qualified: ImplicitUsings brings in Microsoft.AspNetCore.Builder, whose own
               SessionOptions makes the bare name ambiguous in this project. */
            var options = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 1 };
            _session = new InferenceSession(modelPath, options);

            /* Supply only what the graph declares. A sentence-transformers export usually wants
               token_type_ids as well as the ids and the mask, but several omit it, and feeding a
               model an input it never declared is an error rather than a no-op. */
            _wantsAttentionMask = _session.InputMetadata.ContainsKey(AttentionMaskName);
            _wantsTokenTypeIds = _session.InputMetadata.ContainsKey(TokenTypeIdsName);

            if (!_session.InputMetadata.ContainsKey(InputIdsName))
            {
                _logger?.LogWarning(
                    "The model at '{ModelPath}' declares no '{Input}' input, so it is not a text encoder this service can drive. Document retrieval will rank with BM25.",
                    modelPath, InputIdsName);
                Unload();
                return;
            }

            /* One real forward pass at construction rather than trusting the graph's declared
               shapes: an export commonly leaves the hidden size symbolic, and a model that loads
               but cannot run has to be discovered here instead of on a user's first upload. */
            (string? outputName, int dimensions) = Probe();
            _outputName = outputName;
            Dimensions = dimensions;

            if (_outputName == null || Dimensions <= 0)
            {
                _logger?.LogWarning(
                    "The model at '{ModelPath}' produced no per-token output to mean-pool. Document retrieval will rank with BM25.",
                    modelPath);
                Unload();
                return;
            }

            Name = DescribeModel(modelPath);
            _logger?.LogInformation(
                "Local embedding engine '{Engine}' loaded from '{ModelPath}': {Dimensions} dimensions, up to {MaxTokens} tokens per text, batches of {BatchSize}.",
                Name, modelPath, Dimensions, _maxTokens, _batchSize);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "The local embedding model at '{ModelPath}' could not be loaded. Document retrieval will rank with BM25.",
                modelPath);
            Unload();
        }
    }

    public string Name { get; private set; }

    public bool IsAvailable => _session != null && _tokenizer != null && _outputName != null && Dimensions > 0;

    public int Dimensions { get; private set; }

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (!IsAvailable || texts == null || texts.Count == 0)
            return Task.FromResult<IReadOnlyList<float[]>>(Array.Empty<float[]>());

        /* Off the calling thread: embedding a long document is arithmetic measured in hundreds of
           milliseconds, and the turn that asked for it is holding a request open. */
        return Task.Run<IReadOnlyList<float[]>>(() =>
        {
            try
            {
                return EmbedBatches(texts, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex,
                    "Embedding {Count} texts with '{Engine}' failed; the caller falls back to BM25.", texts.Count, Name);
                return Array.Empty<float[]>();
            }
        }, cancellationToken);
    }

    public void Dispose() => Unload();

    /// <summary>
    /// A name for logs and the provenance notice, taken from the model's own path: the file name,
    /// or its directory when the file is called something generic like <c>model.onnx</c>.
    /// </summary>
    private static string DescribeModel(string modelPath)
    {
        string name = Path.GetFileNameWithoutExtension(modelPath);

        if (name.Length == 0
            || name.Equals("model", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("model_", StringComparison.OrdinalIgnoreCase))
        {
            string? directory = Path.GetFileName(Path.GetDirectoryName(modelPath));
            if (!string.IsNullOrEmpty(directory))
                name = directory;
        }

        return name.Length == 0 ? "onnx" : name;
    }

    /// <summary>
    /// Runs one two-token sequence and reports which output carries per-token vectors and how
    /// wide they are. Returns no name and zero width when the model has nothing to mean-pool.
    /// </summary>
    private (string? OutputName, int Dimensions) Probe()
    {
        long[][] ids = { new[] { 101L, 102L } };
        long[][] mask = { new[] { 1L, 1L } };

        using var results = Forward(ids, mask);

        foreach (var value in results)
        {
            var tensor = value.AsTensor<float>();
            if (tensor == null)
                continue;

            var dimensions = tensor.Dimensions;

            // [batch, sequence, hidden] is the per-token output that mean pooling needs.
            if (dimensions.Length == 3 && dimensions[2] > 0)
                return (value.Name, dimensions[2]);
        }

        return (null, 0);
    }

    /// <summary>Builds the inputs the graph declares for one padded batch, and runs it.</summary>
    private IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Forward(long[][] ids, long[][] mask)
    {
        int batch = ids.Length;
        int length = ids[0].Length;
        var shape = new[] { batch, length };

        var flatIds = new long[batch * length];
        var flatMask = new long[batch * length];
        for (int b = 0; b < batch; b++)
        {
            Array.Copy(ids[b], 0, flatIds, b * length, length);
            Array.Copy(mask[b], 0, flatMask, b * length, length);
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputIdsName, new DenseTensor<long>(flatIds, shape))
        };

        if (_wantsAttentionMask)
            inputs.Add(NamedOnnxValue.CreateFromTensor(AttentionMaskName, new DenseTensor<long>(flatMask, shape)));

        // A single segment, so the type ids are all zero; the array is only here to be shaped.
        if (_wantsTokenTypeIds)
            inputs.Add(NamedOnnxValue.CreateFromTensor(TokenTypeIdsName, new DenseTensor<long>(new long[batch * length], shape)));

        return _session!.Run(inputs);
    }

    private IReadOnlyList<float[]> EmbedBatches(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var vectors = new float[texts.Count][];

        for (int offset = 0; offset < texts.Count; offset += _batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int count = Math.Min(_batchSize, texts.Count - offset);
            var encoded = new long[count][];
            for (int i = 0; i < count; i++)
                encoded[i] = Encode(texts[offset + i]);

            // One padded rectangle per batch, padded to the longest text in that batch only.
            int length = encoded.Max(row => row.Length);
            var ids = new long[count][];
            var mask = new long[count][];
            for (int i = 0; i < count; i++)
            {
                ids[i] = new long[length];
                mask[i] = new long[length];
                for (int t = 0; t < length; t++)
                {
                    bool real = t < encoded[i].Length;
                    ids[i][t] = real ? encoded[i][t] : PadTokenId;
                    mask[i][t] = real ? 1L : 0L;
                }
            }

            using var results = Forward(ids, mask);
            var output = results.FirstOrDefault(v => v.Name == _outputName) ?? results.First();
            var tensor = output.AsTensor<float>();
            if (tensor == null)
                throw new InvalidOperationException($"Output '{_outputName}' of the embedding model is not a float tensor.");

            var dense = tensor as DenseTensor<float> ?? tensor.ToDenseTensor();
            var dimensions = dense.Dimensions;
            if (dimensions.Length != 3)
                throw new InvalidOperationException($"Output '{_outputName}' of the embedding model has rank {dimensions.Length}, not 3.");

            int sequence = dimensions[1];
            int hidden = dimensions[2];
            ReadOnlySpan<float> data = dense.Buffer.Span;

            for (int i = 0; i < count; i++)
                vectors[offset + i] = MeanPool(data, i, sequence, hidden, mask[i]);
        }

        return vectors;
    }

    /// <summary>Token ids for one text, truncated to the model's sequence length.</summary>
    private long[] Encode(string? text)
    {
        var encoded = _tokenizer!.EncodeToIds(text ?? string.Empty, true, true);

        int count = Math.Min(encoded.Count, _maxTokens);
        var ids = new long[Math.Max(1, count)];
        for (int i = 0; i < count; i++)
            ids[i] = encoded[i];

        /* Truncation keeps the terminating separator instead of dropping it: the model was
           trained on sequences that end with one, and a truncated sequence that does not looks
           malformed to it. */
        if (encoded.Count > _maxTokens && count > 0)
            ids[count - 1] = encoded[encoded.Count - 1];

        return ids;
    }

    /// <summary>
    /// Mean of the token vectors the mask marks as real, then L2-normalised so a dot product
    /// against another vector is the cosine.
    /// </summary>
    /// <remarks>
    /// Masked mean pooling is what the sentence-transformers models are trained with, so it is
    /// the pooling their similarity scores were calibrated for. Averaging over the whole padded
    /// rectangle instead mixes [PAD] embeddings into the result, and the shorter the text the
    /// larger the share of the vector that is padding rather than content.
    /// </remarks>
    private static float[] MeanPool(ReadOnlySpan<float> data, int item, int sequence, int hidden, long[] mask)
    {
        var sums = new double[hidden];
        int counted = 0;

        int limit = Math.Min(sequence, mask.Length);
        for (int t = 0; t < limit; t++)
        {
            if (mask[t] == 0)
                continue;

            int row = (item * sequence + t) * hidden;
            for (int h = 0; h < hidden; h++)
                sums[h] += data[row + h];

            counted++;
        }

        var vector = new float[hidden];
        if (counted == 0)
            return vector;

        double norm = 0;
        for (int h = 0; h < hidden; h++)
        {
            double mean = sums[h] / counted;
            sums[h] = mean;
            norm += mean * mean;
        }

        norm = Math.Sqrt(norm);
        if (norm <= 0)
            return vector;

        for (int h = 0; h < hidden; h++)
            vector[h] = (float)(sums[h] / norm);

        return vector;
    }

    /// <summary>
    /// Releases the session and puts this service into its unavailable state, so a model that
    /// loaded but could not run is indistinguishable from one that was never there.
    /// </summary>
    private void Unload()
    {
        _session?.Dispose();
        _session = null;
        _tokenizer = null;
        _outputName = null;
        Dimensions = 0;
        Name = "none";
    }
}
