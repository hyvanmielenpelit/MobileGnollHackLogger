namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>An administrator's hold on deleting an account. Active while ReleasedUtc is null.</summary>
public class AccountHold
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser AspNetUser { get; set; } = default!;

    /// <summary>Admin-written; never shown to the user.</summary>
    [MaxLength(512)]
    public string Reason { get; set; } = default!;

    /// <summary>Attribution only.</summary>
    [MaxLength(450)]
    public string PlacedByUserId { get; set; } = default!;

    public DateTime PlacedUtc { get; set; }

    [MaxLength(450)]
    public string? ReleasedByUserId { get; set; }

    public DateTime? ReleasedUtc { get; set; }
}
