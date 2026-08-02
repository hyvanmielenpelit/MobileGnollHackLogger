namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

public class UserAiModel
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    
    public ApplicationUser? AspNetUser { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;  // e.g. "claude-sonnet-4-20250514"

    [MaxLength(128)]
    public string? DisplayName { get; set; }  // User-facing label

    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }

    public int OrderIndex { get; set; }  // For drag-and-drop ordering
}
