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

    [MaxLength(256)]
    public string? ModelDisplayNameUsed { get; set; }

    public ICollection<ChatMessageToolCall> ToolCalls { get; set; } = new List<ChatMessageToolCall>();

    public int? TimeToFirstTokenMs { get; set; }

    public int? TotalDurationMs { get; set; }
}
