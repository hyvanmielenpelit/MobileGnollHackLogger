using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Overseer.Services;

public class ModelMetadataService
{
    private readonly Dictionary<string, List<ModelCatalogEntry>> _providerCatalogs = new(StringComparer.OrdinalIgnoreCase);

    public ModelMetadataService()
    {
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

    public bool IsWhitelisted(string provider, string modelId)
    {
        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(modelId))
            return false;

        if (!_providerCatalogs.TryGetValue(provider, out var catalogEntries))
            return false;

        foreach (var entry in catalogEntries)
        {
            if (entry.Prefixes == null) continue;
            foreach (var prefix in entry.Prefixes)
            {
                if (modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = modelId.Substring(prefix.Length);
                    if (IsVersionSuffix(suffix))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool IsVersionSuffix(string suffix)
    {
        if (suffix.Length == 0)
            return true; /* exact match */

        if (suffix[0] != '-')
            return false;

        if (suffix.Length == 1)
            return false; /* lone hyphen */

        for (int i = 1; i < suffix.Length; i++)
        {
            char c = suffix[i];
            if (!char.IsDigit(c) && c != '-' && c != '.')
                return false;
        }
        return true;
    }

    public ModelMetadata GetMetadata(string provider, string modelId)
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

        ModelCatalogEntry? bestEntry = null;
        int maxPrefixLength = 0;

        if (!string.IsNullOrEmpty(provider) && _providerCatalogs.TryGetValue(provider, out var catalogEntries))
        {
            foreach (var entry in catalogEntries)
            {
                if (entry.Prefixes == null) continue;
                foreach (var prefix in entry.Prefixes)
                {
                    if (modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (prefix.Length > maxPrefixLength)
                        {
                            maxPrefixLength = prefix.Length;
                            bestEntry = entry;
                        }
                    }
                }
            }
        }

        if (bestEntry != null)
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
}
