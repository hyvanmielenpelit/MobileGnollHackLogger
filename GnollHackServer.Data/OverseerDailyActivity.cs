namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Chats and messages created on one UTC day by one user, or, on the row with no user, the day's incognito chats
/// and messages. Counted when they are created, so purging chats never changes it. Holds no content.
/// </summary>
public class OverseerDailyActivity
{
    public long Id { get; set; }

    public DateOnly DateUtc { get; set; }

    /// <summary>Null on the day's anonymous incognito row.</summary>
    [MaxLength(450)]
    public string? AspNetUserId { get; set; }
    public ApplicationUser? AspNetUser { get; set; }

    public int ChatsCreated { get; set; }

    /// <summary>Subset of ChatsCreated.</summary>
    public int ConfidentialChatsCreated { get; set; }

    /// <summary>Subset of ChatsCreated: created by the GnollHack client or a snapshot attach.</summary>
    public int GnollHackChatsCreated { get; set; }

    /// <summary>Visible messages the user sent.</summary>
    public int UserMessages { get; set; }

    /// <summary>Saved assistant replies.</summary>
    public int AssistantMessages { get; set; }

    /// <summary>Anonymous row only.</summary>
    public int IncognitoChatsCreated { get; set; }

    /// <summary>Anonymous row only.</summary>
    public int IncognitoUserMessages { get; set; }
}
