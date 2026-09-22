using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Overseer.Services;

public class ModelMetadataService
{
    private readonly Dictionary<string, List<ModelCatalogEntry>> _providerCatalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ModelMetadataService>? _logger;
    private readonly ConcurrentDictionary<string, byte> _warnedUnknownVersions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How a model ID relates to the catalog prefix it matched.</summary>
    private enum MatchKind
    {
        None,
        /* claude-opus-5-5 for claude-opus-5-5 */
        Exact,
        /* gpt-5.4-2026-03-05 for gpt-5.4: the same model, pinned */
        Snapshot,
        /* gemini-3.5-flash-preview for gemini-3.5-flash: never offered, but described by its family */
        Variant,
        /* claude-opus-5-6 for claude-opus-5: a different model with no entry of its own */
        UnknownVersion
    }

    public ModelMetadataService(ILogger<ModelMetadataService>? logger = null)
    {
        _logger = logger;
        LoadCatalogs();
    }

    private void LoadCatalogs()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var resourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "OpenAI", "Overseer.Services.ModelCatalogs.OpenAiModelCatalog.json" },
            { "Anthropic", "Overseer.Services.ModelCatalogs.AnthropicModelCatalog.json" },
            { "Google", "Overseer.Services.ModelCatalogs.GoogleModelCatalog.json" }
        };

        foreach (var (provider, resourceName) in resourceMap)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var entries = JsonSerializer.Deserialize<List<ModelCatalogEntry>>(json, serializerOptions);

            if (entries == null) continue;

            _providerCatalogs[provider] = entries;
        }
    }

    /// <summary>
    /// The loaded catalog entries for one provider, or an empty list for an unknown provider. Exposed so a
    /// test can assert facts about what the shipped catalogs actually declare — a mistyped service-tier key,
    /// for instance, produces no error at all, just quiet 1.0x mispricing.
    /// </summary>
    internal IReadOnlyList<ModelCatalogEntry> GetCatalogEntries(string provider)
    {
        return _providerCatalogs.TryGetValue(provider, out var entries)
            ? entries
            : Array.Empty<ModelCatalogEntry>();
    }

    /// <summary>
    /// Whether the model picker offers the model: an exact catalog prefix, or one followed by a
    /// dated snapshot suffix. A model that looks like a newer version of a catalogued one is
    /// refused and logged once, so a missing catalog entry is visible rather than mislabelled.
    /// </summary>
    public bool IsWhitelisted(string provider, string modelId)
    {
        var (entry, prefix, kind) = Match(provider, modelId);

        if (kind == MatchKind.UnknownVersion && _warnedUnknownVersions.TryAdd($"{provider}|{modelId}", 0))
        {
            _logger?.LogWarning(
                "Model {ModelId} from {Provider} looks like a new version of catalog entry {Prefix} ({DisplayName}) and is hidden from the model picker until it has its own catalog entry.",
                modelId, provider, prefix, entry?.DisplayName);
        }

        return kind is MatchKind.Exact or MatchKind.Snapshot;
    }

    /// <summary>
    /// The longest catalog prefix that ends where the model ID ends or at a hyphen, and how the rest
    /// of the ID relates to it. A shorter prefix is never used instead of the longest one.
    /// </summary>
    private (ModelCatalogEntry? Entry, string? Prefix, MatchKind Kind) Match(string provider, string modelId)
    {
        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(modelId)
            || !_providerCatalogs.TryGetValue(provider, out var catalogEntries))
        {
            return (null, null, MatchKind.None);
        }

        ModelCatalogEntry? bestEntry = null;
        string? bestPrefix = null;

        foreach (var entry in catalogEntries)
        {
            if (entry.Prefixes == null) continue;
            foreach (var prefix in entry.Prefixes)
            {
                if (string.IsNullOrEmpty(prefix) || prefix.Length <= (bestPrefix?.Length ?? 0))
                    continue; /* on a tie the first entry in file order wins */

                if (!modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (modelId.Length > prefix.Length && modelId[prefix.Length] != '-')
                    continue; /* gpt-5.4 must not match gpt-5.45 */

                bestEntry = entry;
                bestPrefix = prefix;
            }
        }

        if (bestEntry == null || bestPrefix == null)
            return (null, null, MatchKind.None);

        return (bestEntry, bestPrefix, Classify(modelId.Substring(bestPrefix.Length)));
    }

    /// <param name="suffix">Empty, or starting with '-'.</param>
    private static MatchKind Classify(string suffix)
    {
        if (suffix.Length == 0)
            return MatchKind.Exact;

        var body = suffix.Substring(1);
        if (IsSnapshot(body))
            return MatchKind.Snapshot;

        int end = body.IndexOf('-');
        var firstSegment = end < 0 ? body : body.Substring(0, end);
        if (firstSegment.Length > 0 && firstSegment.All(c => char.IsAsciiDigit(c) || c == '.'))
            return MatchKind.UnknownVersion;

        return MatchKind.Variant;
    }

    /* YYYYMMDD (Anthropic), YYYY-MM-DD (OpenAI) or NNN (Google) */
    private static bool IsSnapshot(string body)
    {
        return body.Length switch
        {
            8 => AllDigits(body, 0, 8),
            10 => AllDigits(body, 0, 4) && body[4] == '-' && AllDigits(body, 5, 2) && body[7] == '-' && AllDigits(body, 8, 2),
            3 => AllDigits(body, 0, 3),
            _ => false
        };
    }

    private static bool AllDigits(string s, int start, int length)
    {
        for (int i = start; i < start + length; i++)
        {
            if (!char.IsAsciiDigit(s[i]))
                return false;
        }
        return true;
    }

    public virtual ModelMetadata GetMetadata(string provider, string modelId)
    {
        var metadata = new ModelMetadata
        {
            Id = modelId,
            Description = modelId, // Fallback is the string id itself
            SupportedThinkingLevels = new List<string>()
        };

        if (string.IsNullOrEmpty(modelId))
        {
            return metadata;
        }

        var (bestEntry, _, kind) = Match(provider, modelId);

        // Variant is accepted so hand-typed and custom-endpoint IDs keep their family's metadata.
        if (bestEntry != null && kind is MatchKind.Exact or MatchKind.Snapshot or MatchKind.Variant)
        {
            metadata.DisplayName = bestEntry.DisplayName;
            metadata.Description = bestEntry.DisplayName;
            metadata.ReleaseDate = bestEntry.ReleaseDate;
            metadata.SupportedThinkingLevels = new List<string>(bestEntry.ThinkingLevels ?? new List<string>());
            metadata.SupportedReasoningModes = new List<string>(bestEntry.ReasoningModes ?? new List<string>());
            metadata.SupportedReasoningSummaries = new List<string>(bestEntry.ReasoningSummaries ?? new List<string>());
            metadata.ContextWindowSize = bestEntry.ContextWindowSize;
            metadata.MaxOutputTokens = bestEntry.MaxOutputTokens;
            metadata.MaxInputTokens = metadata.ContextWindowSize - metadata.MaxOutputTokens;
            metadata.SupportsSubAgentCoordination = bestEntry.SupportsSubAgentCoordination;
            metadata.SupportsSubAgentExecution = bestEntry.SupportsSubAgentExecution;
            metadata.DefaultPricing = bestEntry.Pricing;
            return metadata;
        }

        return metadata;
    }
}

public class ModelMetadata
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
    public List<string> SupportedThinkingLevels { get; set; } = new List<string>();
    public List<string> SupportedReasoningModes { get; set; } = new List<string>();
    public List<string> SupportedReasoningSummaries { get; set; } = new List<string>();
    
    public int ContextWindowSize { get; set; }
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public bool SupportsSubAgentCoordination { get; set; } = true;
    public bool SupportsSubAgentExecution { get; set; } = true;
    public ModelCatalogPricing? DefaultPricing { get; set; }
}
