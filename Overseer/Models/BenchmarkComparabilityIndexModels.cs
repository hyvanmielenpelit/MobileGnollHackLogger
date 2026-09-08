namespace Overseer.Models;

using System;
using System.Collections.Generic;

// DTOs for the comparability index: what the model-comparison picker is told about the runs and
// groups it is offering, before anything is compared.
//
// The index answers one question — *which of these may share a chart?* — and answers it with the
// same taxonomy the comparison itself uses, so a source the picker files under the largest
// condition is a source Compare will chart. Everything here is arithmetic over stored rows.

/// <summary>What to index. Each run id and each group id is one offered source.</summary>
public class BenchmarkComparabilityIndexRequest
{
    public List<long> RunIds { get; set; } = new();

    public List<long> GroupIds { get; set; } = new();
}

/// <summary>One offered source, and the condition it belongs to.</summary>
public class BenchmarkComparabilityIndexEntryDto
{
    /// <summary>`run:12` or `group:3` — the same key the comparison uses for the same source.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>`Run` or `Group`.</summary>
    public string SourceKind { get; set; } = string.Empty;

    public long SourceId { get; set; }

    /// <summary>1-based, ordered by descending cohort size then first appearance. 0 = not assigned.</summary>
    public int ConditionOrdinal { get; set; }

    /// <summary>"Condition A" …, or "Self-inconsistent" for a group whose own runs disagree.</summary>
    public string ConditionLabel { get; set; } = string.Empty;

    /// <summary>The must-match signature this source was bucketed by; empty when it has no runs.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>A group whose members differ on a must-match or a model-axis key. Never chartable.</summary>
    public bool SelfInconsistent { get; set; }

    public List<string> SelfInconsistentKeys { get; set; } = new();

    /// <summary>Must-match keys on which this source differs from the largest condition, with both values.</summary>
    public List<BenchmarkComparabilityDifferenceDto> DifferencesFromLargest { get; set; } = new();

    /// <summary>The two degrading key values, so the picker can warn before Compare.</summary>
    public string QuestionParallelism { get; set; } = string.Empty;

    public string PricingSnapshot { get; set; } = string.Empty;
}

/// <summary>One cohort of sources that agree on every must-match key.</summary>
public class BenchmarkComparabilityConditionDto
{
    public int Ordinal { get; set; }

    public string Label { get; set; } = string.Empty;

    public int SourceCount { get; set; }

    public int RunCount { get; set; }
}

/// <summary>
/// The offered sources split into conditions, with the legend a picker needs to explain the split.
/// </summary>
public class BenchmarkComparabilityIndexDto
{
    public DateTime ComputedAtUtc { get; set; }

    /// <summary>One per offered source, in the order the request named them: runs, then groups.</summary>
    public List<BenchmarkComparabilityIndexEntryDto> Entries { get; set; } = new();

    /// <summary>The conditions, largest first. Ordinal 1 is the baseline a comparison would pick.</summary>
    public List<BenchmarkComparabilityConditionDto> Conditions { get; set; } = new();

    /// <summary>The must-match key values of the largest condition, for the legend.</summary>
    public Dictionary<string, string> LargestConditionKeyValues { get; set; } = new();

    public List<string> MustMatchKeyNames { get; set; } = new();

    public List<string> ModelAxisKeyNames { get; set; } = new();

    public List<string> DegradingKeyNames { get; set; } = new();
}
