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

/// <summary>
/// One comparability key as a condition holds it: what the key is, and its value.
///
/// <para>This is the methods statement of a set of figures, one line at a time — the machine name
/// the rest of the product uses, the phrase an operator reads it by, and the value every source in
/// the condition agreed on.</para>
/// </summary>
public class BenchmarkComparabilityKeyValueDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>A short noun phrase — "Question suite", "Candidate system prompt".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>One line: what a difference on this key would mean for a comparison.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>`Fundamental`, `Candidate`, `Instrument` or `SpeedAndCost`.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>`Text`, `Identifier`, `Hash`, `Json` or `List`.</summary>
    public string ValueKind { get; set; } = string.Empty;

    /// <summary>The canonical value compared for equality, verbatim. Never abbreviated here.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>A friendlier rendering where the server knows one the client cannot derive; else null.</summary>
    public string? DisplayValue { get; set; }
}

/// <summary>One cohort of sources that agree on every must-match key.</summary>
public class BenchmarkComparabilityConditionDto
{
    public int Ordinal { get; set; }

    public string Label { get; set; } = string.Empty;

    public int SourceCount { get; set; }

    public int RunCount { get; set; }

    /// <summary>The must-match signature every source in this cohort shares; the citable short form of it.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// The most recent <c>StartedAtUtc</c> among the cohort's runs, so a cohort that is the majority
    /// yet describes a superseded instrument is visibly stale. Null when the cohort has no runs.
    /// </summary>
    public DateTime? NewestRunStartedAtUtc { get; set; }
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

    /// <summary>
    /// The must-match keys of the largest condition, in canonical key order, each with the label,
    /// description and value kind that let a legend read as a methods statement.
    /// </summary>
    public List<BenchmarkComparabilityKeyValueDto> LargestConditionKeys { get; set; } = new();

    /// <summary>
    /// How the reference condition was chosen, in one sentence. Server-owned so that the text a
    /// legend prints cannot drift from the tie-break the bucketing actually applies.
    /// </summary>
    public string ReferenceSelectionRule { get; set; } = string.Empty;

    public List<string> MustMatchKeyNames { get; set; } = new();

    public List<string> ModelAxisKeyNames { get; set; } = new();

    public List<string> DegradingKeyNames { get; set; } = new();
}
