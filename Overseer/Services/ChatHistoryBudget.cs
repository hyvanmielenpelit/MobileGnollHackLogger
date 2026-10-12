namespace Overseer.Services;

/// <summary>Where a chat turn's history input limit came from.</summary>
public enum ChatHistoryLimitSource
{
    /// <summary>The Max Input Tokens set on the configuration or user model.</summary>
    Explicit,

    /// <summary>Kept below the catalog's long-context price threshold.</summary>
    LongContextBudget,

    /// <summary>The model's own maximum input from the catalog.</summary>
    ModelMaximum,

    /// <summary>The 100,000-token default for a model the catalog does not size.</summary>
    Fallback
}

/// <summary>A chat turn's history input limit, in estimator units, and its source.</summary>
public readonly record struct ChatHistoryLimit(int Tokens, ChatHistoryLimitSource Source);

/// <summary>
/// Resolves the input limit chat history is truncated to at the start of a turn. For a model whose
/// catalog card bills prompts over a threshold at a higher rate, the turn's starting prompt is kept
/// under that threshold minus room for the turn's own tool loop, which grows the prompt after
/// truncation has run. The limit is in <c>ChatService.EstimateTokens</c> units (characters / 4), so
/// the real-token budget is divided by an estimate factor for tokenizers that count more densely.
/// </summary>
public static class ChatHistoryBudget
{
    /// <summary>The limit used when the catalog does not give the model a maximum input.</summary>
    public const int FallbackMaxInputTokens = 100000;

    /// <param name="explicitMaxInputTokens">The Max Input Tokens set on the configuration or user
    /// model; a positive value always wins.</param>
    /// <param name="meta">Catalog metadata. Its <see cref="ModelMetadata.DefaultPricing"/> supplies the
    /// threshold, deliberately rather than a resolved price card: a custom price override is flat for
    /// costing, but the provider still bills the long-context rate.</param>
    /// <param name="keepBelowLongContextThreshold">Whether to apply the long-context budget.</param>
    /// <param name="turnHeadroomTokens">Real tokens reserved for the turn's tool loop; negative is 0.</param>
    /// <param name="tokenEstimateFactor">Real tokens per estimated token; zero or negative is 1.0.</param>
    public static ChatHistoryLimit Resolve(
        int? explicitMaxInputTokens, ModelMetadata meta,
        bool keepBelowLongContextThreshold, int turnHeadroomTokens, decimal tokenEstimateFactor)
    {
        if (explicitMaxInputTokens is > 0)
        {
            return new ChatHistoryLimit(explicitMaxInputTokens.Value, ChatHistoryLimitSource.Explicit);
        }

        var result = meta != null && meta.MaxInputTokens > 0
            ? new ChatHistoryLimit(meta.MaxInputTokens, ChatHistoryLimitSource.ModelMaximum)
            : new ChatHistoryLimit(FallbackMaxInputTokens, ChatHistoryLimitSource.Fallback);

        int threshold = meta?.DefaultPricing?.LongContext?.ThresholdInputTokens ?? 0;
        if (!keepBelowLongContextThreshold || threshold <= 0)
        {
            return result;
        }

        decimal factor = tokenEstimateFactor > 0m ? tokenEstimateFactor : 1.0m;
        int headroom = Math.Max(0, turnHeadroomTokens);
        decimal budget = Math.Floor((threshold - (decimal)headroom) / factor);

        return budget > 0m && budget < result.Tokens
            ? new ChatHistoryLimit((int)budget, ChatHistoryLimitSource.LongContextBudget)
            : result;
    }
}
