using System.Text.RegularExpressions;

namespace Overseer.Services;

public class ModelMetadataService
{
    public ModelMetadata GetMetadata(string modelId)
    {
        var metadata = new ModelMetadata
        {
            Id = modelId,
            Description = modelId, // Fallback is the string id itself
            SupportedThinkingLevels = new List<string>()
        };

        // Strict 2026 Model Pattern Matching for OpenAI
        // Matches e.g., gpt-5.6-sol, gpt-5.5, gpt-5.4-mini
        var openaiRegex = new Regex(@"^gpt-5\.([4-6])(?:-(sol|terra|luna|mini|nano|pro))?$");
        var openaiMatch = openaiRegex.Match(modelId);
        if (openaiMatch.Success)
        {
            var version = openaiMatch.Groups[1].Value;
            var tier = openaiMatch.Groups[2].Success ? char.ToUpper(openaiMatch.Groups[2].Value[0]) + openaiMatch.Groups[2].Value.Substring(1) : "";
            metadata.Description = string.IsNullOrWhiteSpace(tier) ? $"OpenAI GPT-5.{version} Model" : $"OpenAI GPT-5.{version} {tier}";
            metadata.SupportedThinkingLevels = new List<string> { "low", "medium", "high" };
            return metadata;
        }

        // Strict 2026 Model Pattern Matching for Anthropic
        // Matches e.g., claude-5-opus, claude-sonnet-5, claude-4.6, claude-4.8-sonnet
        var anthropicRegex = new Regex(@"^claude-(?:(opus|sonnet|fable|mythos)-)?(5|4\.[6-8])(?:-(opus|sonnet|fable|mythos))?$");
        var anthropicMatch = anthropicRegex.Match(modelId);
        if (anthropicMatch.Success)
        {
            var prefixTier = anthropicMatch.Groups[1].Value;
            var version = anthropicMatch.Groups[2].Value;
            var suffixTier = anthropicMatch.Groups[3].Value;
            
            var tierStr = !string.IsNullOrEmpty(prefixTier) ? prefixTier : suffixTier;
            var formattedTier = !string.IsNullOrEmpty(tierStr) ? char.ToUpper(tierStr[0]) + tierStr.Substring(1) : "Model";
            
            metadata.Description = $"Claude {version} {formattedTier}";
            metadata.SupportedThinkingLevels = new List<string> { "minimal", "low", "medium", "high", "max" };
            return metadata;
        }

        // Strict 2026 Model Pattern Matching for Google Gemini
        // Matches e.g., gemini-3.6-flash, gemini-3.5-flash-lite, gemini-3.5-flash-cyber, gemini-3.1-pro
        var googleRegex = new Regex(@"^gemini-3\.([1-6])-(pro|flash|flash-lite|flash-cyber)$");
        var googleMatch = googleRegex.Match(modelId);
        if (googleMatch.Success)
        {
            var version = googleMatch.Groups[1].Value;
            var rawTier = googleMatch.Groups[2].Value;
            
            // Title case the tier parts (e.g. flash-lite -> Flash-Lite)
            var formattedTier = string.Join("-", rawTier.Split('-').Select(part => char.ToUpper(part[0]) + part.Substring(1)));
            
            metadata.Description = $"Gemini 3.{version} {formattedTier}";
            metadata.SupportedThinkingLevels = new List<string> { "minimal", "low", "medium", "high" };
            return metadata;
        }

        return metadata;
    }
}

public class ModelMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SupportedThinkingLevels { get; set; } = new List<string>();
}
