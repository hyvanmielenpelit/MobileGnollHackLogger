namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class SystemAiUsageLog
{
    public long Id { get; set; }

    public long SystemAiApiConfigurationId { get; set; }
    public SystemAiApiConfiguration SystemAiApiConfiguration { get; set; } = default!;

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser AspNetUser { get; set; } = default!;

    public DateTime TimestampUtc { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
}
