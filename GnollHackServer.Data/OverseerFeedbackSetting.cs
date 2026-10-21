namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One setting of one of the two Overseer feedback switches, which offer or withhold
/// <c>report_feedback</c> in the production chat and in GnollBench.
///
/// <para>Append-only: the current state of a switch is the row of its <see cref="Target"/> with the
/// highest <see cref="Id"/>. Rows are never updated or deleted, so each switch's history stays
/// readable. With no row, <c>Feedback:Enabled</c> in the configuration seeds the chat switch and the
/// GnollBench switch is on.</para>
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
