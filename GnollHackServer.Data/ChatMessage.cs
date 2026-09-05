namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class ChatMessage
{
    public long Id { get; set; }
    
    public long ChatSessionId { get; set; }
    public ChatSession? ChatSession { get; set; }
    
    [MaxLength(32)]
    public string? Role { get; set; }
    
    public string? Content { get; set; }
    
    public DateTime TimestampUtc { get; set; }
    
    public int? TokensUsed { get; set; }
    
    public bool IsHidden { get; set; } = false;

    [MaxLength(64)]
    public string? ProviderUsed { get; set; }

    [MaxLength(128)]
    public string? ModelUsed { get; set; }

    [MaxLength(32)]
    public string? ThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? ReasoningModeUsed { get; set; }

    [MaxLength(64)]
    public string? ServiceTierUsed { get; set; }

    /// <summary>
    /// The service tier the provider actually served the request on, as reported by the
    /// provider. Null when the provider did not report one. Distinct from
    /// <see cref="ServiceTierUsed"/>, which records what Overseer requested — the two use
    /// different value spaces per provider (OpenAI requests "auto"/"fast" and serves
    /// "default"/"priority"; Anthropic requests "auto" and serves "standard"/"priority").
    /// </summary>
    [MaxLength(64)]
    public string? ActualServiceTierUsed { get; set; }

    [MaxLength(256)]
    public string? ModelDisplayNameUsed { get; set; }

    public ICollection<ChatMessageToolCall> ToolCalls { get; set; } = new List<ChatMessageToolCall>();

    public int? TimeToFirstTokenMs { get; set; }

    public int? TotalDurationMs { get; set; }

    /// <summary>
    /// Prompt tokens the provider reported for the <b>final</b> model call of this turn — the
    /// last "usage" report, not the sum across tool iterations. Includes cache reads, which
    /// still occupy the context window. This is the conversation's context occupancy going into
    /// the reply. Null for messages saved before this column existed, or when the provider
    /// reported no usage.
    /// </summary>
    public int? ContextPromptTokens { get; set; }

    /// <summary>
    /// Output tokens the provider reported for the final model call of this turn.
    /// <c>ContextPromptTokens + ContextOutputTokens</c> is the context occupied once this reply
    /// is part of the history.
    /// </summary>
    public int? ContextOutputTokens { get; set; }

    /// <summary>
    /// The context window of the model that produced this reply, as recorded in the provider
    /// model catalog at the time of the reply. Stored per message so that switching models
    /// mid-session, or a later catalog change, does not retroactively rewrite history.
    /// </summary>
    public int? ContextWindowTokens { get; set; }

    /// <summary>
    /// The input-token ceiling Overseer applied when truncating history for this turn
    /// (<c>MaxInputTokens</c> override, else <c>ContextWindowTokens - MaxOutputTokens</c>).
    /// This, not <see cref="ContextWindowTokens"/>, is where history truncation begins.
    /// </summary>
    public int? ContextInputLimitTokens { get; set; }

    /// <summary>
    /// Whole-turn token accounting, summed across every tool iteration — not the final model
    /// call alone. <see cref="ContextPromptTokens"/> and <see cref="ContextOutputTokens"/>
    /// describe the last call only and exist to size the context window; costing a
    /// multi-iteration turn from them understates it badly. <see cref="TokensUsed"/> is the
    /// pre-existing input+output sum and is kept for compatibility.
    /// </summary>
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadTokens { get; set; }
    public int? CacheCreationTokens { get; set; }

    /// <summary>
    /// Cost of this turn at the prices in force when it ran. Null when no price was known for
    /// the model. Snapshotted, never recomputed: a price change must not silently rewrite
    /// history.
    /// </summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>"custom" or "catalog" — which price produced <see cref="EstimatedCost"/>.</summary>
    [MaxLength(16)]
    public string? PricingSource { get; set; }
}
