namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class ChatSession
{
    public long Id { get; set; }
    
    public string? AspNetUserId { get; set; }
    public ApplicationUser? AspNetUser { get; set; }
    
    [MaxLength(256)]
    public string? Title { get; set; }
    
    public DateTime CreatedUtc { get; set; }
    
    public DateTime LastMessageUtc { get; set; }
}
