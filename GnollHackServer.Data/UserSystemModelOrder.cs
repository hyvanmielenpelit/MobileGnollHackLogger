namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// A user's position for one system model in their model list. Separate from assignments, so ordering never
/// grants access to a configuration.
/// </summary>
public class UserSystemModelOrder
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser AspNetUser { get; set; } = default!;

    /// <summary>Attribution only, not a foreign key.</summary>
    public long SystemAiApiConfigurationId { get; set; }

    public int OrderIndex { get; set; }
}
