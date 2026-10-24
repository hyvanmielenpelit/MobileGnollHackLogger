namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One setting of the snapshot guard's mode: <c>Off</c>, <c>Observe</c> or <c>Enforce</c>.
///
/// <para>Append-only: the current mode is the row with the highest <see cref="Id"/>. Rows are never
/// updated or deleted, so the history stays readable. With no row, <c>SnapshotGuard:Mode</c> in the
/// configuration seeds the mode.</para>
/// </summary>
public class SnapshotGuardSetting
{
    public long Id { get; set; }

    [MaxLength(16)]
    public string Mode { get; set; } = "";

    public DateTime SetAtUtc { get; set; }

    [MaxLength(450)]
    public string? SetByUserId { get; set; }
}
