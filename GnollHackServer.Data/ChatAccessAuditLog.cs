namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>What kind of access a <see cref="ChatAccessAuditLog"/> row records.</summary>
/// <remarks>
/// Stored as a string rather than an integer. An audit row outlives the code that wrote it, and
/// a reviewer reading the table years later should not need this enum to interpret it — nor
/// should reordering the enum silently change what old rows mean.
/// </remarks>
public enum ChatAccessAction
{
    /// <summary>A conversation's messages were read.</summary>
    SessionRead,

    /// <summary>An attachment's bytes were served.</summary>
    AttachmentRead,

    /// <summary>A conversation was exported as portable data.</summary>
    Export,

    /// <summary>A conversation and its files were permanently destroyed.</summary>
    Erasure
}

/// <summary>
/// One recorded access to a conversation or an attachment.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a suspected incident cannot be investigated at all: request logging says a URL
/// was fetched, and cost accounting says tokens were spent, but nothing said <em>who read whose
/// conversation</em>.
/// </para>
/// <para>
/// <b>"Append-only" here means the application never updates or deletes a row, and there is no
/// code path that can.</b> That is worth stating precisely rather than implying more: the
/// database account Overseer runs under can still write this table, so a reviewer who does not
/// trust the application cannot rely on it. Genuine immutability needs storage the application
/// cannot rewrite — a different credential, an append-only sink, or an external log service —
/// and that is a deployment change, not a code change. Retention removes whole rows past
/// <c>ChatRetentionSettings:AuditLogRetentionDays</c>, which is a policy choice and is also not
/// immutability.
/// </para>
/// <para>
/// The row records identifiers and never content: no message text, no title, no filename. A
/// journal of who read what must not become a second copy of what they read.
/// </para>
/// </remarks>
public class ChatAccessAuditLog
{
    public long Id { get; set; }

    /// <summary>When the access happened.</summary>
    public DateTime OccurredUtc { get; set; }

    /// <summary>
    /// What was done. A string in the database; <see cref="ChatAccessAction"/> is the vocabulary.
    /// </summary>
    [MaxLength(32)]
    public string Action { get; set; } = default!;

    /// <summary>
    /// Who did it. Nullable because a background maintenance pass has no user, and recording a
    /// stand-in id would make an automated purge indistinguishable from a person's.
    /// </summary>
    [MaxLength(450)]
    public string? ActorUserId { get; set; }

    /// <summary>
    /// The actor's user name at the time, so the row stays readable after the account is gone.
    /// </summary>
    [MaxLength(256)]
    public string? ActorUserName { get; set; }

    /// <summary>Whether the actor held administrator rights when they did it.</summary>
    public bool ActorWasAdmin { get; set; }

    /// <summary>
    /// Whose data it was. Equal to <see cref="ActorUserId"/> for ordinary self-access; the
    /// interesting rows are the ones where it is not.
    /// </summary>
    [MaxLength(450)]
    public string? SubjectUserId { get; set; }

    /// <summary>The conversation, where there is one. Not a foreign key — see the remarks.</summary>
    /// <remarks>
    /// Deliberately no relationship to <see cref="ChatSession"/>: a cascade would delete the
    /// audit row along with the conversation, which is exactly backwards. The record of a
    /// deletion has to outlive what was deleted.
    /// </remarks>
    public long? ChatSessionId { get; set; }

    /// <summary>The attachment, where there is one. Also not a foreign key.</summary>
    public long? ChatMessageAttachmentId { get; set; }

    /// <summary>
    /// The session reference in wire form, so an incognito session's access is recorded too.
    /// </summary>
    /// <remarks>
    /// An ephemeral session has no row and therefore no id, but it is still someone's
    /// conversation being read. This is the only field that can identify one, and it identifies
    /// nothing about its contents.
    /// </remarks>
    [MaxLength(64)]
    public string? SessionRef { get; set; }

    /// <summary>Whether the conversation was in Confidentiality Mode.</summary>
    public bool WasConfidential { get; set; }

    /// <summary>The caller's address, where the access came through a request.</summary>
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// A short note for a reviewer: a count, an outcome, a reason. <b>Never content.</b>
    /// </summary>
    [MaxLength(512)]
    public string? Detail { get; set; }
}
