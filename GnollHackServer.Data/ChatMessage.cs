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
}
