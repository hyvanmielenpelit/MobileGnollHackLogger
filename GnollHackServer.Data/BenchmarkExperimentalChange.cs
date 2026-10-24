namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// A GnollBench experimental change written in Overseer: a text-only change (tool description edits and
/// instructions appended to the system prompt) offered to benchmark runs and never to the live chat.
/// Built-in changes live in <c>Overseer/BenchmarkChanges/</c> and are not stored here.
///
/// <para>No foreign keys and no user ids. Runs refer to a change by <see cref="ChangeId"/> inside the
/// stamp in <c>BenchmarkRun.InstructionVariant</c>.</para>
/// </summary>
public class BenchmarkExperimentalChange
{
    public long Id { get; set; }

    /// <summary>Kebab-case, ending <c>-v&lt;N&gt;</c>; unique across built-in and stored changes; never changes.</summary>
    [Required]
    [MaxLength(32)]
    public string ChangeId { get; set; } = "";

    [Required]
    [MaxLength(60)]
    public string Title { get; set; } = "";

    [Required]
    [MaxLength(220)]
    public string Summary { get; set; } = "";

    [MaxLength(220)]
    public string? ExpectedEffect { get; set; }

    /// <summary>JSON array of <c>{ "tool", "mode": "add" | "replace", "text" }</c>, text normalized.</summary>
    public string? DescriptionEditsJson { get; set; }

    /// <summary>Normalized text appended to the end of the system prompt.</summary>
    public string? Instructions { get; set; }

    /// <summary>The change hash (64 lowercase hex characters), recomputed on every save.</summary>
    [Required]
    [MaxLength(64)]
    public string ContentHash { get; set; } = "";

    /// <summary>The change this one was copied from as a new version.</summary>
    [MaxLength(32)]
    public string? CopiedFromChangeId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Compared on every update as the optimistic concurrency check.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set when the first run is stamped with the change; never cleared. Locks the hashed text.</summary>
    public DateTime? LockedAtUtc { get; set; }

    public DateTime? RetiredAtUtc { get; set; }
}
