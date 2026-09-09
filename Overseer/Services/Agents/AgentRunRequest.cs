namespace Overseer.Services.Agents;

using Overseer.Services.Providers;
using Overseer.Services.Tools;

public class AgentRunRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ApiKey { get; set; }

    /// <summary>
    /// Where this run's provider requests go and how they authenticate. Defaults to the
    /// provider's official public endpoint, so a caller that does not configure one behaves
    /// exactly as before custom endpoints existed.
    /// </summary>
    public Overseer.Services.Providers.AiEndpointDescriptor Endpoint { get; set; }
        = Overseer.Services.Providers.AiEndpointDescriptor.Official;
    public string? ModelDisplayName { get; set; }
    public string? SystemPrompt { get; set; }
    public string? FrozenPrefix { get; set; }
    public string? SessionPrefix { get; set; }
    public string? VolatileSuffix { get; set; }
    public SegmentedPrompt? SegmentedPrompt { get; set; }
    public string? PromptCacheKey { get; set; }
    public string? CredentialKey { get; set; }
    public TimeSpan? PermitWaitTimeout { get; set; }
    public List<object> SeedHistory { get; set; } = new();
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ReasoningSummary { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int MaxToolIterations { get; set; } = 22;
    public int MaxParallelTools { get; set; } = 6;
    public int MaxParallelClientTools { get; set; } = 1;
    public bool EnableWebSearch { get; set; }
    public bool EnableToolUse { get; set; } = true;
    public bool EnableSubAgents { get; set; } = false;
    public bool EnableClientTools { get; set; }
    public bool EnableGameActions { get; set; }
    public ToolExecutionContext ToolExecutionContext { get; set; } = new();
    public long? SystemModelId { get; set; }
    public string? AgentName { get; set; }
    public string? ParentToolCallId { get; set; }
    public int Depth { get; set; }
    public int AgentDepth { get => Depth; set => Depth = value; }
    public int MaxAgentDepth { get; set; } = 1;
    public IReadOnlyList<string>? AllowedTools { get; set; }
    public IReadOnlyList<string>? AllowedToolNames { get => AllowedTools; set => AllowedTools = value; }
    public bool ShowDebugLog { get; set; }
    public AgentRunBudget? Budget { get; set; }

    /// <summary>
    /// The turn's DLP vault, or null when no class of masking is enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loop uses it for exactly one thing: masking a tool result before it re-enters
    /// <c>messageHistory</c>. A tool that read a file, searched a corpus or called a sub-agent
    /// can return a credential, and that result is part of the next request's prompt — so
    /// masking only the user's own message would let a secret reach the provider by the longer
    /// route.
    /// </para>
    /// <para>
    /// It travels on the request rather than being resolved inside the loop because one vault
    /// has to serve the whole turn: the same secret must get the same token wherever it appears,
    /// and a vault created per tool iteration would number each occurrence separately.
    /// </para>
    /// </remarks>
    public Overseer.Services.Privacy.Dlp.DlpTokenVault? DlpVault { get; set; }

    public IAiProvider? AiProvider { get; set; }
}
