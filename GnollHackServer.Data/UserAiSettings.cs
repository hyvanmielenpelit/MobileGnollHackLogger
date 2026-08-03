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
    public int ShowThoughtsAndTools { get; set; } = 1;

    public int? MaxResultLength { get; set; }
    public int? MaxCallsPerSession { get; set; }
    public int? MaxToolIterations { get; set; }

    public bool EnableWebSearch { get; set; } = true;
    public bool EnableToolUse { get; set; } = true;
    public bool EnableClientTools { get; set; } = true;
    public bool EnableGameActions { get; set; } = false;

    public bool AllowMultipleModels { get; set; } = false;

    public long? TitleGenerationModelId { get; set; }
}
