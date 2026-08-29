namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class UserAiSettings
{
    [Key]
    [ForeignKey("AspNetUser")]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser? AspNetUser { get; set; }
    
    public bool SpoilerFreeMode { get; set; } = true;
    public bool ShowSourceCodeReferences { get; set; } = false;
    public bool ShowParallelBadge { get; set; } = true;
    public int ShowThoughtsAndTools { get; set; } = 1;

    public int? MaxResultLength { get; set; }
    public int? MaxCallsPerSession { get; set; }
    public int? MaxToolIterations { get; set; }
    public int? MaxParallelToolCalls { get; set; }

    public bool EnableWebSearch { get; set; } = true;
    public bool EnableToolUse { get; set; } = true;
    public bool EnableSubAgents { get; set; } = false;
    public bool EnableClientTools { get; set; } = true;
    public bool EnableGameActions { get; set; } = false;

    public long? TitleGenerationModelId { get; set; }
    public long? TitleGenerationSystemModelId { get; set; }
    public bool TitleGenerationDisabled { get; set; } = false;

    public int? RequestTimeout { get; set; }
}
