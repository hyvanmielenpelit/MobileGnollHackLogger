namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One setting of the Overseer feedback switch, which offers or withholds <c>report_feedback</c> in
/// the production chat.
///
/// <para>Append-only: the current state is the row with the highest <see cref="Id"/>. Rows are never
/// updated or deleted, so the history stays readable. With no row, <c>Feedback:Enabled</c> in the
/// configuration decides.</para>
/// </summary>
public class OverseerFeedbackSetting
{
    public long Id { get; set; }

    public bool Enabled { get; set; }

    public DateTime SetAtUtc { get; set; }

    [MaxLength(450)]
    public string? SetByUserId { get; set; }
}
