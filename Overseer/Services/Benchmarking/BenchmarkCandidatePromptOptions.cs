namespace Overseer.Services.Benchmarking;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;
using Overseer.Services;

/// <summary>
/// The configuration of the production chat system prompt that the candidate is graded under.
///
/// The benchmark does not use a bespoke question-answering prompt: BenchmarkService calls
/// ChatService.BuildSystemPrompt, so every quality verdict in a report is a verdict on the prompt
/// real users get. Which *configuration* of it was graded was previously a set of literals at one
/// call site and appeared in no report — so a Completeness score could not be read against the
/// terseness instruction that produced it. Defaults here are exactly run 11's values; changing one
/// is a deliberate act that the run snapshot then records.
/// </summary>
public sealed record BenchmarkCandidatePromptOptions
{
    [JsonPropertyName("verboseMode")]
    public bool VerboseMode { get; init; }              // false

    [JsonPropertyName("spoilerFreeMode")]
    public bool SpoilerFreeMode { get; init; }          // false

    [JsonPropertyName("overseerMode")]
    public int OverseerMode { get; init; }              // 0 — Gameplay Help

    [JsonPropertyName("enableToolUse")]
    public bool EnableToolUse { get; init; } = true;

    [JsonPropertyName("enableWebSearch")]
    public bool EnableWebSearch { get; init; }          // false

    [JsonPropertyName("allowSourceCodeReferences")]
    public bool AllowSourceCodeReferences { get; init; } = true;

    [JsonPropertyName("enableSubAgents")]
    public bool EnableSubAgents { get; init; }          // false

    [JsonPropertyName("isGameOn")]
    public bool IsGameOn { get; init; }                 // false

    [JsonPropertyName("developerMode")]
    public bool DeveloperMode { get; init; }            // false

    [JsonPropertyName("hasMessageHistory")]
    public bool HasMessageHistory { get; init; }        // false

    [JsonPropertyName("hasWikiContext")]
    public bool HasWikiContext { get; init; }           // false — chat pre-injects; the benchmark does not

    [JsonPropertyName("hasGameSnapshot")]
    public bool HasGameSnapshot { get; init; }          // false — defaulted or from suite (suiteHasBoard)

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes with stable property ordering and camelCase property names so two configurations
    /// can be compared for string equality.
    /// </summary>
    public string ToCanonicalJson()
    {
        return JsonSerializer.Serialize(this, CanonicalOptions);
    }

    public static BenchmarkCandidatePromptOptions FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BenchmarkCandidatePromptOptions();
        }

        try
        {
            return JsonSerializer.Deserialize<BenchmarkCandidatePromptOptions>(json, CanonicalOptions)
                ?? new BenchmarkCandidatePromptOptions();
        }
        catch
        {
            return new BenchmarkCandidatePromptOptions();
        }
    }

    /// <summary>
    /// Builds the system prompt using this configuration and the specified parallel execution mode.
    /// </summary>
    public string BuildSystemPrompt(ChatService chatService, ParallelExecutionMode parallelMode)
    {
        return chatService.BuildSystemPrompt(
            wikiContext: Array.Empty<string>(),
            spoilerFreeMode: SpoilerFreeMode,
            verboseMode: VerboseMode,
            isGameOn: IsGameOn,
            developerMode: DeveloperMode,
            overseerMode: OverseerMode,
            hasGameSnapshot: HasGameSnapshot,
            hasMessageHistory: HasMessageHistory,
            clientSettings: null,
            enableToolUse: EnableToolUse,
            enableWebSearch: EnableWebSearch,
            allowSourceCodeReferences: AllowSourceCodeReferences,
            enableSubAgents: EnableSubAgents,
            parallelMode: parallelMode);
    }

    /// <summary>
    /// Returns a one-line human-readable summary of this configuration.
    /// </summary>
    public string Describe()
    {
        string style = VerboseMode ? "detailed (verboseMode: true)" : "concise (verboseMode: false)";
        string mode = OverseerMode == 0 ? "Gameplay Help" : $"Mode {OverseerMode}";
        return $"{mode}, {style}, tools: {(EnableToolUse ? "enabled" : "disabled")}, web search: {(EnableWebSearch ? "enabled" : "disabled")}, subagents: {(EnableSubAgents ? "enabled" : "disabled")}, source code references: {(AllowSourceCodeReferences ? "allowed" : "disallowed")}";
    }
}
