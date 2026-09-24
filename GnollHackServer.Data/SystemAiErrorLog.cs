namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class SystemAiErrorLog
{
    public long Id { get; set; }

    // Attribution only, not a foreign key: deleting a configuration never touches its logs.
    public long SystemAiApiConfigurationId { get; set; }

    // The configuration's identity when the error was recorded. Null on legacy rows.
    [MaxLength(64)]
    public string? Provider { get; set; }

    [MaxLength(128)]
    public string? ModelId { get; set; }

    [MaxLength(256)]
    public string? ModelDisplayName { get; set; }

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    public int? HttpStatusCode { get; set; }

    public DateTime TimestampUtc { get; set; }

    public bool IsDismissed { get; set; }

    [MaxLength(450)]
    public string? DismissedByUserId { get; set; }
    public ApplicationUser? DismissedByUser { get; set; }

    public DateTime? DismissedAtUtc { get; set; }
}
