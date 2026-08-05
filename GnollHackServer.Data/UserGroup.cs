namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

public class UserGroup
{
    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser AspNetUser { get; set; } = default!;

    public long GroupId { get; set; }
    public Group Group { get; set; } = default!;
}
