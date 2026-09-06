namespace MobileGnollHackLogger.Data;

using System;

/// <summary>
/// One run's membership of one analysis group. A separate entity rather than a skip navigation so
/// the row can carry when it was added, which is what tells a reader whether a stored analysis
/// still covers the group's current membership.
/// </summary>
public class BenchmarkRunGroupMember
{
    public long Id { get; set; }

    public long BenchmarkRunGroupId { get; set; }
    public BenchmarkRunGroup BenchmarkRunGroup { get; set; } = default!;

    public long BenchmarkRunId { get; set; }
    public BenchmarkRun BenchmarkRun { get; set; } = default!;

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}
