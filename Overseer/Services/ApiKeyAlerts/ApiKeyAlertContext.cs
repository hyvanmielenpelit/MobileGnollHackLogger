namespace Overseer.Services.ApiKeyAlerts;

public enum ApiKeyUsage { Chat, SubAgent, TitleGeneration }

/// <summary>
/// The user-facing context of a run that may report an API key failure. Present only on runs a
/// user's chat turn started; its absence is what keeps benchmarks and every other caller from
/// reporting. Carries ids and flags, never message content.
/// </summary>
public sealed record ApiKeyAlertContext(
    ApiKeyUsage Usage, Privacy.SessionRef Session, string UserId, string? UserName, string? AgentName = null)
{
    public ApiKeyAlertContext ForSubAgent(string agentName) =>
        this with { Usage = ApiKeyUsage.SubAgent, AgentName = agentName };
}
