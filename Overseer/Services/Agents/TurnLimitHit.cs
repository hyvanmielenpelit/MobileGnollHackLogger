namespace Overseer.Services.Agents;

/// <summary>The stable codes of what can end or cut a chat turn. Stored on the message and the event row.</summary>
public static class TurnLimitCodes
{
    public const string NoFinalAnswer = "no_final_answer";
    public const string ResearchRounds = "research_rounds";
    public const string ModelCalls = "model_calls";
    public const string ToolBudget = "tool_budget";
    public const string ToolOutput = "tool_output";
    public const string AnswerLength = "answer_length";
    public const string TimeLimit = "time_limit";
    public const string ProviderBusy = "provider_busy";
    public const string ProviderOverloaded = "provider_overloaded";
    public const string ProviderTimeout = "provider_timeout";
    public const string ProviderError = "provider_error";
    public const string ConnectionLost = "connection_lost";
    public const string ApiKeyRejected = "api_key_rejected";
    public const string ApiKeyBalance = "api_key_balance";

    /// <summary>The call to the provider failed, so the attempt's status and error code describe the hit.</summary>
    public static bool IsProviderFailure(string code) => code is
        ProviderBusy or ProviderOverloaded or ProviderTimeout or ProviderError
        or ConnectionLost or ApiKeyRejected or ApiKeyBalance;
}

/// <summary>One limit or provider failure a chat turn ran into. Numbers and sanitized codes only.</summary>
public sealed class TurnLimitHit
{
    public required string Code { get; init; }

    public int? LimitValue { get; init; }

    public int? ObservedValue { get; init; }

    public int? HttpStatus { get; set; }

    /// <summary>Restricted to [A-Za-z0-9_.-].</summary>
    public string? ProviderErrorCode { get; set; }

    /// <summary>The recovery call went on to answer, so the hit is recorded and the user is shown no notice for it.</summary>
    public bool Recovered { get; set; }
}
