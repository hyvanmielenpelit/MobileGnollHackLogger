namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// A user's decision on whether one system-provided model may fund their confidential chats.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="UserAiApiKey.UserTrustsForConfidential"/> for a model the
/// operator provides rather than one the user pays for. A row exists only once the user has
/// decided; its absence is "undecided", which is the state the AskWhenUnclear gate prompts
/// about — once per model, not once per turn.
/// </para>
/// <para>
/// A table of its own rather than two columns on <see cref="UserSystemAiApiConfiguration"/>,
/// because a user can reach a system model through a group assignment and have no per-user
/// row at all. The decision is about the model, and it must not depend on how the model was
/// provisioned.
/// </para>
/// </remarks>
public class UserSystemModelConfidentialTrust
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;

    public ApplicationUser? AspNetUser { get; set; }

    public long SystemAiApiConfigurationId { get; set; }

    public SystemAiApiConfiguration? SystemAiApiConfiguration { get; set; }

    /// <summary>
    /// True accepts the model for confidential chats, false refuses it in every gate mode.
    /// Not nullable: the row itself carries the undecided state by not existing.
    /// </summary>
    public bool UserTrustsForConfidential { get; set; }

    /// <summary>When the decision was last made.</summary>
    public DateTime DecidedUtc { get; set; }
}
