namespace Overseer.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;

public record ModelPricing(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal? CachedInputPerMillion = null);

public class ModelPricingService
{
    private readonly IConfiguration _configuration;

    public ModelPricingService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public virtual ModelPricing? GetPricing(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var section = _configuration.GetSection("ModelPricing");
        if (!section.Exists())
        {
            return null;
        }

        IConfigurationSection? bestMatch = null;
        int maxPrefixLength = -1;

        foreach (var child in section.GetChildren())
        {
            string prefix = child.Key;
            if (modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (prefix.Length > maxPrefixLength)
                {
                    maxPrefixLength = prefix.Length;
                    bestMatch = child;
                }
            }
        }

        if (bestMatch == null)
        {
            return null;
        }

        var inputStr = bestMatch["InputPerMillion"];
        var outputStr = bestMatch["OutputPerMillion"];
        var cachedStr = bestMatch["CachedInputPerMillion"];

        if (decimal.TryParse(inputStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var input) &&
            decimal.TryParse(outputStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var output))
        {
            decimal? cached = null;
            if (decimal.TryParse(cachedStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var c))
            {
                cached = c;
            }

            return new ModelPricing(input, output, cached);
        }

        return null;
    }
}
