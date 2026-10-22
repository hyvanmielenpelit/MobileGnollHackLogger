namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Text;
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

    /// <summary>
    /// The information condition's wire name; null means Production and is omitted from the JSON,
    /// so a production record serializes exactly as it did before conditions existed.
    /// </summary>
    [JsonPropertyName("informationCondition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InformationCondition { get; init; }

    /// <summary>
    /// True when the run was launched with the Feedback Tool setting Withheld, so <c>report_feedback</c>
    /// was not declared to a candidate with a tool loop. Recorded under every information condition, so
    /// two runs launched with the same setting pair across conditions. Omitted from the JSON when
    /// false, so a run offered the tool serializes as it did before the setting existed.
    /// </summary>
    [JsonPropertyName("feedbackToolWithheld")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool FeedbackToolWithheld { get; init; }

    [JsonIgnore]
    public BenchmarkInformationCondition Condition => BenchmarkInformationConditions.FromWire(InformationCondition);

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

    /// <summary>
    /// Computes the comparability signature incorporating the prompt options and the parallel execution mode.
    /// </summary>
    public string ComparabilitySignature(ParallelExecutionMode parallelMode)
    {
        return $"{ToCanonicalJson()}|parallelMode={(int)parallelMode}";
    }

    /// <summary>
    /// <see cref="ComparabilitySignature"/> with the board flag cleared: the part of the prompt
    /// configuration every suite of one battery run must share, since only the board flag differs
    /// between a board suite and a non-board suite by design.
    /// </summary>
    public string BatteryWideSignature(ParallelExecutionMode parallelMode)
        => (this with { HasGameSnapshot = false }).ComparabilitySignature(parallelMode);

    /// <summary>
    /// <see cref="ComparabilitySignature"/> of the record as a production run would hold it: the
    /// condition cleared and tools enabled, because a closed-book record also disables tools. Two
    /// runs that differ only in their information condition share it.
    /// </summary>
    public string ComparabilitySignatureWithoutCondition(ParallelExecutionMode parallelMode)
        => (this with { InformationCondition = null, EnableToolUse = true }).ComparabilitySignature(parallelMode);

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
    /// The source-only notice and then <paramref name="appendix"/> (an instruction variant's text)
    /// follow the production prompt as final paragraphs.
    /// </summary>
    public string BuildSystemPrompt(ChatService chatService, ParallelExecutionMode parallelMode, string? appendix = null)
    {
        string prompt = chatService.BuildSystemPrompt(
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
        return AppendBenchmarkParagraphs(prompt, appendix);
    }

    /// <summary>
    /// The same prompt as <see cref="BuildSystemPrompt"/>, in the three cache segments the
    /// providers key their prompt caches on. The three concatenate to that flat string, which is
    /// what the run snapshot still hashes; the benchmark's own paragraphs end the volatile suffix.
    /// </summary>
    public (string frozenPrefix, string sessionPrefix, string volatileSuffix) BuildSegmentedSystemPrompt(
        ChatService chatService, ParallelExecutionMode parallelMode, string? appendix = null)
    {
        var (frozen, session, volatileSuffix) = chatService.BuildSegmentedSystemPrompt(
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
        // The paragraphs are separated as in the flat prompt, so the three segments still concatenate to it.
        string before = frozen + session + volatileSuffix;
        string after = AppendBenchmarkParagraphs(before, appendix);
        return (frozen, session, volatileSuffix + after[before.Length..]);
    }

    /// <summary>
    /// <paramref name="text"/> followed by the benchmark-only paragraphs: the source-only notice
    /// under that condition, then <paramref name="appendix"/> when given. Unchanged when neither applies.
    /// </summary>
    private string AppendBenchmarkParagraphs(string text, string? appendix)
    {
        var paragraphs = new List<string>();
        if (Condition == BenchmarkInformationCondition.SourceOnly)
        {
            paragraphs.Add(BenchmarkInformationConditions.SourceOnlyNotice);
        }
        if (!string.IsNullOrWhiteSpace(appendix))
        {
            paragraphs.Add(appendix.Trim());
        }
        if (paragraphs.Count == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text);
        foreach (string paragraph in paragraphs)
        {
            if (sb.Length > 0)
            {
                sb.Append(sb[^1] == '\n' ? "\n" : "\n\n");
            }
            sb.Append(paragraph).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns a one-line human-readable summary of this configuration.
    /// </summary>
    public string Describe()
    {
        string style = VerboseMode ? "detailed (verboseMode: true)" : "concise (verboseMode: false)";
        string mode = OverseerMode == 0 ? "Gameplay Help" : $"Mode {OverseerMode}";
        string description = $"{mode}, {style}, tools: {(EnableToolUse ? "enabled" : "disabled")}, web search: {(EnableWebSearch ? "enabled" : "disabled")}, subagents: {(EnableSubAgents ? "enabled" : "disabled")}, source code references: {(AllowSourceCodeReferences ? "allowed" : "disallowed")}";
        if (FeedbackToolWithheld)
        {
            description += ", feedback tool: withheld";
        }
        return Condition == BenchmarkInformationCondition.Production
            ? description
            : $"{description}, information condition: {Condition.DisplayName()}";
    }
}
