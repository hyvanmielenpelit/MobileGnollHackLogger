namespace Overseer.Models;

using System;

/// <summary>
/// The GnollBench baseline model: the model the Overseer team currently prefers, which Model
/// Comparison and the comparison-wide documents judge the other models against.
/// </summary>
public class BenchmarkBaselineModelDto
{
    /// <summary>The configuration snapshot it was set from; its model-axis key is what a comparison entry is matched by.</summary>
    public long SnapshotId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxOutputTokens { get; set; }

    /// <summary>The configuration it was chosen from, for the UI only; null when that is unknown.</summary>
    public long? SourceConfigurationId { get; set; }

    public DateTime SetAtUtc { get; set; }
}

/// <summary>Sets the baseline model from a live configuration with the Benchmark role; a null id clears it.</summary>
public class BenchmarkBaselineModelSetRequest
{
    public long? ConfigurationId { get; set; }
}

/// <summary>The values of <see cref="BenchmarkModelComparisonDto.BaselineModelState"/>.</summary>
public static class BenchmarkBaselineModelStates
{
    /// <summary>No baseline model is set.</summary>
    public const string NotSet = "NotSet";

    /// <summary>No entry of the comparison is the baseline model.</summary>
    public const string NotInComparison = "NotInComparison";

    /// <summary>Only excluded entries are the baseline model: their runs are not comparable with the others.</summary>
    public const string Excluded = "Excluded";

    /// <summary>A charted entry is the baseline model.</summary>
    public const string Included = "Included";
}
