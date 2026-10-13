namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class SystemAiUsageLog
{
    public long Id { get; set; }

    // Attribution only, not a foreign key: deleting a configuration never touches its logs.
    public long SystemAiApiConfigurationId { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser AspNetUser { get; set; } = default!;

    public DateTime TimestampUtc { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;

    /// <summary>The configuration's display name when the call was made. Null on legacy rows.</summary>
    [MaxLength(256)]
    public string? ModelDisplayName { get; set; }

    /// <summary>The thinking level sent; "" when the request carried none. Null on rows written before the column existed.</summary>
    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

    /// <summary>Estimated cost in USD of exactly the calls this row records; null when unpriced.</summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>"catalog", "custom" or "mixed"; null when unpriced.</summary>
    [MaxLength(16)]
    public string? PricingSource { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }
    public int? TotalDurationMs { get; set; }

    public int RoleContext { get; set; } = 1; // 1 = Chat, 2 = Title Generation, 3 = SubAgent, 4 = Benchmark, 5 = Question Generation, 6 = Rubric Check, 7 = Suite Description, 8 = Report Pack
}
