namespace Overseer.Services;

using System.Text.Json;
using Overseer.Services.Providers;

/// <summary>A message's estimated size: its text at characters / 4 and its images by their dimensions.</summary>
public readonly record struct ChatMessageEstimate(int TextTokens, int ImageTokens)
{
    public int Total => TextTokens + ImageTokens;
}

/// <summary>
/// Sizes a chat turn's messages for budgeting. Providers bill images by resolution, so an image counts by its
/// pixel dimensions and never by its base64 length. There is one conservative rule per provider rather than a
/// per-model table; overestimating trims history slightly early, which is harmless. One instance serves one
/// turn, because truncation asks about the same message several times and a base64 walk is not free.
/// </summary>
public sealed class ChatTokenEstimator
{
    public const int FallbackImageTokens = 2000;

    // The first 786 KB of an image. A frame header further in than that uses the flat estimate.
    private const int MaxHeaderBase64Chars = 1024 * 1024;

    private readonly string? _provider;
    private readonly Dictionary<object, ChatMessageEstimate> _estimates = new(ReferenceEqualityComparer.Instance);

    public ChatTokenEstimator(string? provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// Tokens for one image under the provider's published rule, with its largest limits. An unknown provider
    /// or unknown dimensions give <see cref="FallbackImageTokens"/>.
    /// </summary>
    public static int EstimateImage(string? provider, int width, int height)
    {
        if (width <= 0 || height <= 0) return FallbackImageTokens;

        switch (provider?.ToLowerInvariant())
        {
            case "anthropic":
                // The high-resolution tier, so a standard-tier model is overestimated and never under.
                double scale = Math.Min(1.0, 2576.0 / Math.Max(width, height));
                long fittedWidth = (long)Math.Ceiling(width * scale);
                long fittedHeight = (long)Math.Ceiling(height * scale);
                return (int)Math.Min(4784, CeilDiv(fittedWidth, 28) * CeilDiv(fittedHeight, 28));

            case "openai":
                long patches = CeilDiv(width, 32) * CeilDiv(height, 32);
                return (int)Math.Min(36000, CeilDiv(patches * 6, 5));

            case "google":
                long tiles = width <= 384 && height <= 384 ? 1 : CeilDiv(width, 768) * CeilDiv(height, 768);
                return (int)Math.Min(int.MaxValue, Math.Max(1120, tiles * 258));

            default:
                return FallbackImageTokens;
        }
    }

    /// <summary>
    /// A message's size. Plain string content is its length / 4. Anything else is walked as JSON, recognizing
    /// the image shapes of Anthropic (<c>source.data</c>), Google (<c>inlineData.data</c>) and OpenAI
    /// (<c>image_url</c>); an unrecognized string counts as text, so a new shape errs high.
    /// </summary>
    public ChatMessageEstimate EstimateMessage(object message)
    {
        if (message is string text) return new ChatMessageEstimate(text.Length / 4, 0);

        if (_estimates.TryGetValue(message, out var known)) return known;

        ChatMessageEstimate estimate;
        if (ProviderHelper.GetProperty(message, "content") is string content)
        {
            estimate = new ChatMessageEstimate(content.Length / 4, 0);
        }
        else
        {
            long chars = 0;
            long imageTokens = 0;
            Walk(JsonSerializer.SerializeToElement(message), ref chars, ref imageTokens);
            estimate = new ChatMessageEstimate((int)Math.Min(int.MaxValue, chars / 4), (int)Math.Min(int.MaxValue, imageTokens));
        }

        _estimates[message] = estimate;
        return estimate;
    }

    public int Estimate(object message) => EstimateMessage(message).Total;

    private void Walk(JsonElement element, ref long chars, ref long imageTokens)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                chars += element.GetString()?.Length ?? 0;
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, ref chars, ref imageTokens);
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (TryGetImageBase64(property, out string base64))
                    {
                        imageTokens += EstimateBase64Image(base64);
                    }
                    else
                    {
                        Walk(property.Value, ref chars, ref imageTokens);
                    }
                }
                break;
        }
    }

    private static bool TryGetImageBase64(JsonProperty property, out string base64)
    {
        base64 = string.Empty;

        if (property.Name is "source" or "inlineData"
            && property.Value.ValueKind == JsonValueKind.Object
            && property.Value.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.String)
        {
            base64 = data.GetString() ?? string.Empty;
            return true;
        }

        if (property.Name == "image_url" && property.Value.ValueKind == JsonValueKind.String)
        {
            string url = property.Value.GetString() ?? string.Empty;
            int comma = url.IndexOf(',');
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            {
                base64 = url[(comma + 1)..];
                return true;
            }
        }

        return false;
    }

    private int EstimateBase64Image(string base64)
    {
        var prefix = base64.AsSpan(0, Math.Min(base64.Length, MaxHeaderBase64Chars));
        var bytes = new byte[prefix.Length / 4 * 3 + 3];

        return Convert.TryFromBase64Chars(prefix, bytes, out int written)
            && ImageDimensionReader.TryRead(bytes.AsSpan(0, written), out int width, out int height)
                ? EstimateImage(_provider, width, height)
                : FallbackImageTokens;
    }

    private static long CeilDiv(long value, long divisor) => (value + divisor - 1) / divisor;
}
