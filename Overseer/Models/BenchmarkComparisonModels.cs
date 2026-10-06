namespace Overseer.Models;

using System;
using System.Collections.Generic;

// DTOs for numbered model comparisons: identifying the comparison of a selection, renaming it, and
// listing every comparison with its documents. The number is BenchmarkComparison.Id.

/// <summary>
/// The body of <c>POST model-comparisons/identify</c>: the selection the wizard compared. Runs and
/// groups, or battery results; never both, and never nothing.
/// </summary>
public class BenchmarkComparisonIdentifyRequest
{
    public List<long>? RunIds { get; set; }
    public List<long>? GroupIds { get; set; }
    public List<long>? BatteryRunIds { get; set; }
}

/// <summary>The body of <c>PATCH model-comparisons/{id}</c>; an empty or null name resets to the default name.</summary>
public class BenchmarkComparisonRenameRequest
{
    public string? Name { get; set; }
}

/// <summary>One numbered comparison, shown as <i>Comparison #Id</i>.</summary>
public class BenchmarkComparisonDto
{
    public int Id { get; set; }

    /// <summary>The display name: <see cref="CustomName"/> when set, otherwise <see cref="DefaultName"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The administrator's name; null when the comparison carries its default name.</summary>
    public string? CustomName { get; set; }

    public string DefaultName { get; set; } = string.Empty;
    public int EntryCount { get; set; }

    /// <summary><c>Runs</c> or <c>Batteries</c>.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    /// <summary><c>run:&lt;id&gt;</c>, <c>group:&lt;id&gt;</c> or <c>battery:&lt;id&gt;</c>: runs, then groups, then battery results, each by ascending id.</summary>
    public List<string> EntryKeys { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RenamedAtUtc { get; set; }
}

/// <summary>One comparison in the comparison list, newest first, with how many report documents it has.</summary>
public class BenchmarkComparisonListItemDto
{
    public int Id { get; set; }

    /// <summary>The display name: <see cref="CustomName"/> when set, otherwise <see cref="DefaultName"/>.</summary>
    public string Name { get; set; } = string.Empty;

    public string? CustomName { get; set; }
    public string DefaultName { get; set; } = string.Empty;
    public int EntryCount { get; set; }

    /// <summary><c>Runs</c> or <c>Batteries</c>.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    public int DocumentCount { get; set; }

    /// <summary>When the newest of its documents was written; null when it has none.</summary>
    public DateTime? LastDocumentAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
