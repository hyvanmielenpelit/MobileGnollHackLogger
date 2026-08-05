namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class SystemAiErrorLog
{
    public long Id { get; set; }

    public long SystemAiApiConfigurationId { get; set; }
    public SystemAiApiConfiguration SystemAiApiConfiguration { get; set; } = default!;

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
