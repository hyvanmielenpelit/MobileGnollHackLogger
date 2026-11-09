namespace Overseer.Services;

/// <summary>
/// Resolves the output cap a chat model call is sent: the configured Max Output Tokens, clamped to the
/// catalog's maximum, or the catalog's maximum when none is configured. Null means the provider's own
/// default applies, which the catalog does not know.
/// </summary>
public static class ChatOutputBudget
{
    /// <param name="configuredMaxOutputTokens">The Max Output Tokens set on the configuration or user
    /// model, or on the run request.</param>
    /// <param name="meta">Catalog metadata; a <see cref="ModelMetadata.MaxOutputTokens"/> of 0 or less
    /// means the catalog does not know the model's maximum.</param>
    public static int? Resolve(int? configuredMaxOutputTokens, ModelMetadata? meta)
    {
        int catalogMax = meta?.MaxOutputTokens ?? 0;
        if (!configuredMaxOutputTokens.HasValue)
        {
            return catalogMax > 0 ? catalogMax : null;
        }

        return catalogMax > 0
            ? Math.Min(configuredMaxOutputTokens.Value, catalogMax)
            : configuredMaxOutputTokens;
    }
}
