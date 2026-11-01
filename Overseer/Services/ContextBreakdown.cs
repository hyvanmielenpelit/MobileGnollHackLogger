namespace Overseer.Services;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// What occupied the context at a turn's final model call. The seven categories are estimates scaled to the
/// provider's prompt total, so they always sum to it exactly. Cache and reasoning figures are measured.
/// Numbers only, so the JSON is safe to store in plain text in every mode.
/// </summary>
public sealed record ContextBreakdown
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")] public int Version { get; init; } = CurrentVersion;
    [JsonPropertyName("systemPrompt")] public int SystemPrompt { get; init; }
    [JsonPropertyName("toolDefinitions")] public int ToolDefinitions { get; init; }
    [JsonPropertyName("gameContext")] public int GameContext { get; init; }
    [JsonPropertyName("history")] public int History { get; init; }
    [JsonPropertyName("documents")] public int Documents { get; init; }
    [JsonPropertyName("images")] public int Images { get; init; }
    [JsonPropertyName("toolActivity")] public int ToolActivity { get; init; }
    [JsonPropertyName("cacheRead")] public int CacheRead { get; init; }
    [JsonPropertyName("cacheWrite")] public int CacheWrite { get; init; }
    [JsonPropertyName("reasoning")] public int Reasoning { get; init; }
}

/// <summary>The estimated sizes of a turn's starting prompt, in estimator units. Set only by the chat, so
/// GnollBench and sub-agents do no capture work.</summary>
public sealed record ContextBreakdownSeed(int SystemPrompt, int GameContext, int History, int Documents, int Images);

public static class ContextBreakdownEstimator
{
    /// <summary>
    /// Scales the estimated sizes to the measured prompt total with largest-remainder rounding. Null when
    /// there is no total or nothing to scale.
    /// </summary>
    public static ContextBreakdown? Scale(
        ContextBreakdownSeed seed, int toolDefinitions, int toolActivity,
        int promptTokens, int cacheRead, int cacheWrite, int reasoning)
    {
        long[] weights =
        [
            Math.Max(0, seed.SystemPrompt), Math.Max(0, toolDefinitions), Math.Max(0, seed.GameContext),
            Math.Max(0, seed.History), Math.Max(0, seed.Documents), Math.Max(0, seed.Images), Math.Max(0, toolActivity)
        ];

        long totalWeight = weights.Sum();
        if (promptTokens <= 0 || totalWeight <= 0) return null;

        var shares = new long[weights.Length];
        var remainders = new long[weights.Length];
        long assigned = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            shares[i] = weights[i] * promptTokens / totalWeight;
            remainders[i] = weights[i] * promptTokens % totalWeight;
            assigned += shares[i];
        }

        foreach (int i in Enumerable.Range(0, weights.Length).OrderByDescending(i => remainders[i]).Take((int)(promptTokens - assigned)))
        {
            shares[i]++;
        }

        return new ContextBreakdown
        {
            SystemPrompt = (int)shares[0],
            ToolDefinitions = (int)shares[1],
            GameContext = (int)shares[2],
            History = (int)shares[3],
            Documents = (int)shares[4],
            Images = (int)shares[5],
            ToolActivity = (int)shares[6],
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            Reasoning = reasoning
        };
    }

    public static string? ToJson(ContextBreakdown? breakdown)
        => breakdown == null ? null : JsonSerializer.Serialize(breakdown);

    /// <summary>The stored breakdown, or null when it is missing, malformed or of an unknown version.</summary>
    public static ContextBreakdown? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var breakdown = JsonSerializer.Deserialize<ContextBreakdown>(json);
            return breakdown?.Version == ContextBreakdown.CurrentVersion ? breakdown : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The characters a list of messages takes when serialized.</summary>
    public static long SerializedLength(IEnumerable<object> messages)
        => messages.Sum(m => (long)JsonSerializer.Serialize(m).Length);
}
