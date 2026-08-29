namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

public class ChatMessageToolCall
{
    public long Id { get; set; }
    
    public long ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }
    
    [MaxLength(128)]
    public string? ToolCallId { get; set; }
    
    [MaxLength(256)]
    public string? Name { get; set; }
    
    [MaxLength(256)]
    public string? DisplayName { get; set; }
    
    public string? ArgsText { get; set; }
    
    [MaxLength(32)]
    public string? Status { get; set; }
    
    public string? Result { get; set; }
    
    public string? Error { get; set; }
    
    public int? QueueWaitMs { get; set; }
    
    public int? ExecutionMs { get; set; }
    
    public int SortOrder { get; set; }
    
    [MaxLength(128)]
    public string? AgentName { get; set; }
    
    [MaxLength(128)]
    public string? ParentToolCallId { get; set; }
    
    public int Depth { get; set; }
    
    public int? BatchIndex { get; set; }
}
