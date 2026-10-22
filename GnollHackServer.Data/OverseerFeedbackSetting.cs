namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One setting of the Overseer feedback switch, which offers or withholds <c>report_feedback</c> in
/// the production chat. Rows of the retired <see cref="OverseerFeedbackSwitchTarget.GnollBench"/>
/// target are kept as history and never read.
///
/// <para>Append-only: the current state of the switch is the chat row with the highest
/// <see cref="Id"/>. Rows are never updated or deleted, so the history stays readable. With no chat
/// row, <c>Feedback:Enabled</c> in the configuration seeds the switch.</para>
/// </summary>
public class OverseerFeedbackSetting
{
    public long Id { get; set; }

    public OverseerFeedbackSwitchTarget Target { get; set; }

    public bool Enabled { get; set; }

    public DateTime SetAtUtc { get; set; }

    [MaxLength(450)]
    public string? SetByUserId { get; set; }
}
