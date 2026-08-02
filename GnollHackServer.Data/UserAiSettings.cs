namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class UserAiSettings
{
    [Key]
    [ForeignKey("AspNetUser")]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser? AspNetUser { get; set; }
    
    [MaxLength(64)]
    public string? DefaultProvider { get; set; }
    
    [MaxLength(128)]
    public string? DefaultModel { get; set; }
    
    [MaxLength(2048)]
    public string? EncryptedApiKey { get; set; }
    
    [MaxLength(32)]
    public string? ApiKeyNonce { get; set; }
    
    [MaxLength(32)]
    public string? ApiKeyTag { get; set; }

    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

    public bool SpoilerFreeMode { get; set; } = true;

    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }

    public bool EnableWebSearch { get; set; } = true;
    public bool EnableToolUse { get; set; } = true;
    public bool EnableClientTools { get; set; } = true;
    public bool EnableGameActions { get; set; } = false;

    public bool AllowMultipleModels { get; set; } = false;
}
