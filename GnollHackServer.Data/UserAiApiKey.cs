namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

public class UserAiApiKey
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    
    public ApplicationUser? AspNetUser { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = default!;  // "OpenAI", "Anthropic", "Google"

    [MaxLength(2048)]
    public string? EncryptedApiKey { get; set; }
    
    [MaxLength(32)]
    public string? ApiKeyNonce { get; set; }
    
    [MaxLength(32)]
    public string? ApiKeyTag { get; set; }

    public ParallelExecutionMode ParallelExecutionMode { get; set; } = ParallelExecutionMode.Enabled;
}

