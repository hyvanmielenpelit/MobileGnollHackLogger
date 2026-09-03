namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class BenchmarkGameSnapshot
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    public string SanitizedText { get; set; } = default!;

    [MaxLength(2000)]
    public string? DigestText { get; set; }

    public int CharCount { get; set; }

    [MaxLength(64)]
    public string Sha256 { get; set; } = default!;

    [MaxLength(32)]
    public string CaptureMethod { get; set; } = default!;

    [MaxLength(64)]
    public string? SourceGnollHackVersion { get; set; }

    public string? Notes { get; set; }

    public DateTime? CapturedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}