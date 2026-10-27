namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>The process whose game sign-in throttle holds a block. Stored as a short; the values are part of the stored contract.</summary>
public enum GameSignInBlockSource : short { Website = 1, Overseer = 2 }

/// <summary>The limit a block belongs to: an account and one address, or an account from every address. Stored as a short.</summary>
public enum GameSignInBlockKind : short { Pair = 1, Account = 2 }

/// <summary>
/// The pair or account block of an existing account, mirrored from a process's in-memory game sign-in throttle so
/// an administrator can see and clear it. Never holds a user name.
/// </summary>
public class GameSignInBlock
{
    public long Id { get; set; }

    public GameSignInBlockSource Source { get; set; }

    public GameSignInBlockKind Kind { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser? AspNetUser { get; set; }

    /// <summary>Pair blocks only.</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    public DateTime BlockedUtc { get; set; }

    public DateTime BlockedUntilUtc { get; set; }

    public DateTime? ClearedUtc { get; set; }

    /// <summary>Attribution only, not a foreign key.</summary>
    [MaxLength(450)]
    public string? ClearedByUserId { get; set; }
}
