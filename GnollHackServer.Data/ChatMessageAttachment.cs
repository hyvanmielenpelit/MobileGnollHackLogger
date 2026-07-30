using System;
using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Data;

public class ChatMessageAttachment
{
    public long Id { get; set; }
    
    public long ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }
    
    [MaxLength(256)]
    public string? FileName { get; set; }
    
    [MaxLength(128)]
    public string? ContentType { get; set; }
    
    [MaxLength(1024)]
    public string? RelativePath { get; set; }
}
