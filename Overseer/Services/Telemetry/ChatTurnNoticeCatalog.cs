namespace Overseer.Services.Telemetry;

using System.Text.Json;
using System.Text.RegularExpressions;
using Overseer.Services.Agents;

/// <summary>What the client renders under an assistant message. <c>Data</c> of the <c>turn_notice</c> event, too.</summary>
public sealed record ChatTurnNotice(
    string Code,
    string Severity,
    string Title,
    string Message,
    string Advice,
    string Action,
    long? Reference,
    IReadOnlyDictionary<string, object?>? AdminFacts);

/// <summary>The numbers and sanitized codes behind a notice, stored as <c>ChatMessage.TurnNoticeFactsJson</c>.</summary>
public sealed record ChatTurnNoticeFacts
{
    public string Severity { get; init; } = ChatTurnNoticeCatalog.SeverityWarning;
    public bool SystemKey { get; init; }
    public int? LimitValue { get; init; }
    public int? ObservedValue { get; init; }
    public int? IterationsUsed { get; init; }
    public int? ModelCallCount { get; init; }
    public int? ToolCallCount { get; init; }
    public int? TotalDurationMs { get; init; }
    public int? HttpStatus { get; init; }
    public string? ProviderErrorCode { get; init; }
    public string? ProviderFinishReason { get; init; }
    public string? AssistantPhase { get; init; }
}

/// <summary>
/// The only place with user-facing text about a limit or a provider failure. Holds no numbers, provider
/// names, model output or raw errors, and is rendered from the code at read time, so a wording fix also
/// corrects old messages.
/// </summary>
public static class ChatTurnNoticeCatalog
{
    public const string SeverityWarning = "warning";
    public const string SeverityError = "error";

    public const string ActionTryAgain = "try_again";
    public const string ActionContinue = "continue";
    public const string ActionNewChat = "new_chat";
    public const string ActionNone = "none";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record Entry(string Title, string Message, string Advice, string Action);

    /// <summary>Highest priority first. <c>tool_output</c> is for administrators alone and is not here.</summary>
    private static readonly string[] Priority =
    {
        TurnLimitCodes.ApiKeyRejected,
        TurnLimitCodes.ApiKeyBalance,
        TurnLimitCodes.TimeLimit,
        TurnLimitCodes.ProviderBusy,
        TurnLimitCodes.ProviderOverloaded,
        TurnLimitCodes.ProviderTimeout,
        TurnLimitCodes.ProviderError,
        TurnLimitCodes.ConnectionLost,
        TurnLimitCodes.NoFinalAnswer,
        TurnLimitCodes.ResearchRounds,
        TurnLimitCodes.ModelCalls,
        TurnLimitCodes.ToolBudget,
        TurnLimitCodes.AnswerLength
    };

    private static readonly Dictionary<string, Entry> Entries = new()
    {
        [TurnLimitCodes.NoFinalAnswer] = new("The Overseer stopped before answering",
            "It finished its work without writing a final answer.",
            "Try again, or rephrase your question.", ActionTryAgain),
        [TurnLimitCodes.ResearchRounds] = new("Research limit reached",
            "The Overseer used all the research steps allowed for one question, so this answer may be incomplete.",
            "Ask it to continue, or ask a narrower question.", ActionContinue),
        [TurnLimitCodes.ModelCalls] = new("Research limit reached",
            "The Overseer used all the AI calls allowed for one question, so this answer may be incomplete.",
            "Ask it to continue, or split the question into smaller ones.", ActionContinue),
        [TurnLimitCodes.ToolBudget] = new("Research limit reached for this chat",
            "This chat has used all of its research calls, so this answer may be incomplete.",
            "Start a new chat to keep researching.", ActionNewChat),
        [TurnLimitCodes.AnswerLength] = new("Answer cut short",
            "The answer reached the maximum length.",
            "Ask it to continue.", ActionContinue),
        [TurnLimitCodes.TimeLimit] = new("Time limit reached",
            "The answer took longer than the time allowed and was stopped.",
            "Try again, or ask a narrower question.", ActionTryAgain),
        [TurnLimitCodes.ProviderBusy] = new("The AI service is busy",
            "It is receiving too many requests right now.",
            "Wait a minute, then try again.", ActionTryAgain),
        [TurnLimitCodes.ProviderOverloaded] = new("The AI service is unavailable",
            "It could not take this request right now.",
            "Wait a few minutes and try again, or choose another model.", ActionTryAgain),
        [TurnLimitCodes.ProviderTimeout] = new("The AI service did not respond",
            "It took too long to reply.",
            "Wait a moment, then try again.", ActionTryAgain),
        [TurnLimitCodes.ProviderError] = new("The AI service returned an error",
            "It could not complete this request.",
            "Try again. If it keeps happening, choose another model or report this message.", ActionTryAgain),
        [TurnLimitCodes.ConnectionLost] = new("The connection to the AI service was lost",
            "The answer stopped before it was finished.",
            "Try again.", ActionTryAgain)
    };

    /// <summary>The one code the message's notice shows; hits a recovery call answered do not count.</summary>
    public static string? PrimaryCode(IEnumerable<TurnLimitHit> hits)
    {
        var codes = hits.Where(h => !h.Recovered).Select(h => h.Code).ToHashSet();
        return Priority.FirstOrDefault(codes.Contains);
    }

    /// <summary>Whether <paramref name="content"/> holds anything outside its thought blocks.</summary>
    public static bool HasVisibleAnswer(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return !string.IsNullOrWhiteSpace(Regex.Replace(
            content, "<div class=\"ai-thought\">.*?</div>", string.Empty, RegexOptions.Singleline));
    }

    public static string SerializeFacts(ChatTurnNoticeFacts facts) => JsonSerializer.Serialize(facts, JsonOptions);

    public static ChatTurnNoticeFacts ParseFacts(string? factsJson)
    {
        if (string.IsNullOrWhiteSpace(factsJson)) return new ChatTurnNoticeFacts();
        try
        {
            return JsonSerializer.Deserialize<ChatTurnNoticeFacts>(factsJson, JsonOptions) ?? new ChatTurnNoticeFacts();
        }
        catch (JsonException)
        {
            return new ChatTurnNoticeFacts();
        }
    }

    /// <summary>The event payload, with the shape a stored message's notice has.</summary>
    public static string SerializeNotice(ChatTurnNotice notice) => JsonSerializer.Serialize(notice, JsonOptions);

    /// <summary>The notice for a stored or live turn, or null when the code has no user-facing text.</summary>
    public static ChatTurnNotice? Render(string? code, ChatTurnNoticeFacts facts, long? reference, IReadOnlyDictionary<string, object?>? adminFacts)
    {
        if (code == null) return null;

        var entry = code switch
        {
            TurnLimitCodes.ApiKeyRejected => facts.SystemKey
                ? new Entry("The API key was rejected",
                    "The provider did not accept this model's key.",
                    "Choose another model; an administrator has been told.", ActionNone)
                : new Entry("The API key was rejected",
                    "The provider did not accept your API key.",
                    "Check it under Settings → API Keys.", ActionNone),
            TurnLimitCodes.ApiKeyBalance => facts.SystemKey
                ? new Entry("The API key has no credit",
                    "This model's provider budget is used up.",
                    "Choose another model, or contact the administrator.", ActionNone)
                : new Entry("The API key has no credit",
                    "Your provider account has no credit left.",
                    "Add credit with your provider.", ActionNone),
            _ => Entries.GetValueOrDefault(code)
        };

        return entry == null
            ? null
            : new ChatTurnNotice(code, facts.Severity, entry.Title, entry.Message, entry.Advice, entry.Action, reference, adminFacts);
    }
}
