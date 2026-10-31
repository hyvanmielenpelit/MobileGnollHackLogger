namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>The shared admin task an administrator marked handled, up to and including a sequence.</summary>
public class AdminAlertHandling
{
    /// <summary>The task, such as "snapshot-refusals" or "ai-spend:20261031".</summary>
    [Key]
    [MaxLength(64)]
    public string TaskKey { get; set; } = string.Empty;

    /// <summary>The alert stays hidden while its sequence is at or below this.</summary>
    public long HandledThrough { get; set; }

    /// <summary>The administrator's user id. Has no foreign key, so erasing that account is not blocked.</summary>
    [MaxLength(450)]
    public string? HandledByUserId { get; set; }

    public DateTime HandledAtUtc { get; set; }
}
