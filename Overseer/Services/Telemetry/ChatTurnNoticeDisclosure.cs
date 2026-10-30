namespace Overseer.Services.Telemetry;

using Overseer.Services.Agents;

/// <summary>
/// What a viewer may see behind a notice. Everyone gets the catalog text. A user on their own API key also
/// gets the HTTP status and the sanitized provider code, because the account is theirs. An administrator
/// gets every fact, and on a live turn only the bounded provider body, which is never stored.
/// </summary>
public static class ChatTurnNoticeDisclosure
{
    public static IReadOnlyDictionary<string, object?>? For(
        bool isAdmin, string code, ChatTurnNoticeFacts facts, string? liveProviderErrorBody = null)
    {
        var disclosed = new Dictionary<string, object?>();

        if (isAdmin)
        {
            Add(disclosed, "limitValue", facts.LimitValue);
            Add(disclosed, "observedValue", facts.ObservedValue);
            Add(disclosed, "iterationsUsed", facts.IterationsUsed);
            Add(disclosed, "modelCallCount", facts.ModelCallCount);
            Add(disclosed, "toolCallCount", facts.ToolCallCount);
            Add(disclosed, "totalDurationMs", facts.TotalDurationMs);
            Add(disclosed, "providerFinishReason", facts.ProviderFinishReason);
            Add(disclosed, "assistantPhase", facts.AssistantPhase);
        }

        if (isAdmin || (!facts.SystemKey && TurnLimitCodes.IsProviderFailure(code)))
        {
            Add(disclosed, "httpStatus", facts.HttpStatus);
            Add(disclosed, "providerErrorCode", facts.ProviderErrorCode);
        }

        if (isAdmin)
        {
            Add(disclosed, "providerErrorBody", liveProviderErrorBody);
        }

        return disclosed.Count == 0 ? null : disclosed;
    }

    /// <summary>The notice as this viewer may read it, or null when the message has none.</summary>
    public static ChatTurnNotice? Render(bool isAdmin, string? code, string? factsJson, long? reference)
    {
        if (code == null) return null;
        var facts = ChatTurnNoticeCatalog.ParseFacts(factsJson);
        return ChatTurnNoticeCatalog.Render(code, facts, reference, For(isAdmin, code, facts));
    }

    private static void Add(Dictionary<string, object?> facts, string key, object? value)
    {
        if (value != null) facts[key] = value;
    }
}
