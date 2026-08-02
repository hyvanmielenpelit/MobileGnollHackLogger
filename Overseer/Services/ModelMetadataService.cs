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

        var openaiRegex = new Regex(@"^gpt-5\.([4-6])(?:-(sol|terra|luna|mini|nano|pro))?$");
        var openaiMatch = openaiRegex.Match(modelId);
        if (openaiMatch.Success)
        {
            var version = openaiMatch.Groups[1].Value;
            var tier = openaiMatch.Groups[2].Success ? char.ToUpper(openaiMatch.Groups[2].Value[0]) + openaiMatch.Groups[2].Value.Substring(1) : "";
            metadata.Description = string.IsNullOrWhiteSpace(tier) ? $"OpenAI GPT-5.{version} Model" : $"OpenAI GPT-5.{version} {tier}";
            metadata.SupportedThinkingLevels = new List<string> { "low", "medium", "high" };
            metadata.ContextWindowSize = 1048576;
            metadata.MaxOutputTokens = 128000;
            metadata.MaxInputTokens = metadata.ContextWindowSize - metadata.MaxOutputTokens;
            return metadata;
        }

        var openai4Regex = new Regex(@"^gpt-4o(?:-(mini))?$");
        var openai4Match = openai4Regex.Match(modelId);
        if (openai4Match.Success)
        {
            var isMini = openai4Match.Groups[1].Success;
            metadata.Description = isMini ? "OpenAI GPT-4o Mini" : "OpenAI GPT-4o";
            metadata.ContextWindowSize = 128000;
            metadata.MaxOutputTokens = 16384;
            metadata.MaxInputTokens = metadata.ContextWindowSize - metadata.MaxOutputTokens;
            return metadata;
        }

        var anthropicRegex = new Regex(@"^claude-(?:(opus|sonnet|fable|mythos)-)?(5|4(?:[.-])[6-8])(?:-(opus|sonnet|fable|mythos))?$");
        var anthropicMatch = anthropicRegex.Match(modelId);
        if (anthropicMatch.Success)
        {
            var prefixTier = anthropicMatch.Groups[1].Value;
            var version = anthropicMatch.Groups[2].Value.Replace("-", ".");
            var suffixTier = anthropicMatch.Groups[3].Value;
            
            var tierStr = !string.IsNullOrEmpty(prefixTier) ? prefixTier : suffixTier;
            var formattedTier = !string.IsNullOrEmpty(tierStr) ? char.ToUpper(tierStr[0]) + tierStr.Substring(1) : "Model";
            
            metadata.Description = $"Claude {version} {formattedTier}";
            metadata.SupportedThinkingLevels = new List<string> { "minimal", "low", "medium", "high", "max" };
            metadata.ContextWindowSize = 1000000;
            metadata.MaxOutputTokens = 128000;
            metadata.MaxInputTokens = metadata.ContextWindowSize - metadata.MaxOutputTokens;
            return metadata;
        }

        var anthropic3Regex = new Regex(@"^claude-3-5-(sonnet|haiku).*$");
        var anthropic3Match = anthropic3Regex.Match(modelId);
        if (anthropic3Match.Success)
        {
            var tierStr = anthropic3Match.Groups[1].Value;
            var formattedTier = !string.IsNullOrEmpty(tierStr) ? char.ToUpper(tierStr[0]) + tierStr.Substring(1) : "Model";
            metadata.Description = $"Claude 3.5 {formattedTier}";
            metadata.ContextWindowSize = 200000;
            metadata.MaxOutputTokens = 8192;
            metadata.MaxInputTokens = metadata.ContextWindowSize - metadata.MaxOutputTokens;
            return metadata;
        }

        var googleRegex = new Regex(@"^gemini-(2\.5|3(?:\.[1-6])?)-(pro|flash(?:-lite|-cyber|-tts)?)(?:-(.*))?$");
        var googleMatch = googleRegex.Match(modelId);
        if (googleMatch.Success)
        {
            var version = googleMatch.Groups[1].Value;
            var rawTier = googleMatch.Groups[2].Value;
            var suffixes = googleMatch.Groups[3].Value;
            
            // Title case the tier parts (e.g. flash-lite -> Flash-Lite)
            var formattedTier = string.Join("-", rawTier.Split('-').Select(part => char.ToUpper(part[0]) + part.Substring(1)));
            var formattedSuffixes = string.IsNullOrEmpty(suffixes) ? "" : " " + string.Join(" ", suffixes.Split('-').Select(part => char.ToUpper(part[0]) + part.Substring(1)));
            
            metadata.Description = $"Gemini {version} {formattedTier}{formattedSuffixes}";
            if (version.StartsWith("3"))
            {
                metadata.SupportedThinkingLevels = new List<string> { "minimal", "low", "medium", "high" };
            }
            
            if (version == "3.5")
            {
                metadata.ContextWindowSize = 2097152;
            }
            else
            {
                metadata.ContextWindowSize = 1048576;
            }
            metadata.MaxOutputTokens = 65536;
            metadata.MaxInputTokens = metadata.ContextWindowSize - metadata.MaxOutputTokens;
            
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
    
    public int ContextWindowSize { get; set; }
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
}
