namespace Overseer.Models;

using System;
using System.Collections.Generic;

/// <summary>
/// The query parameters shared by the GnollBench feedback endpoints. Every list is comma-separated; the parsed form is
/// <c>GnollBenchFeedbackFilter</c>.
/// </summary>
public sealed class GnollBenchFeedbackFilterQuery
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>new | acknowledged | resolved | regressed | dismissed.</summary>
    public string? Status { get; set; }

    /// <summary>accepted (the default) | all | refused.</summary>
    public string? Accepted { get; set; }
    public string? Category { get; set; }

    /// <summary>The tested model ids.</summary>
    public string? Model { get; set; }
    public string? Provider { get; set; }
    public string? Run { get; set; }
    public string? Series { get; set; }

    /// <summary>Multi-Run Analysis group ids.</summary>
    public string? Group { get; set; }

    /// <summary>Battery run ids.</summary>
    public string? Battery { get; set; }

    /// <summary>Model batch ids.</summary>
    public string? Batch { get; set; }
    public string? Suite { get; set; }
    public string? Question { get; set; }

    /// <summary>production | sourceOnly | closedBook.</summary>
    public string? Condition { get; set; }

    /// <summary>Stored corpus conditions; <c>live</c> for none.</summary>
    public string? Corpus { get; set; }

    /// <summary>Variant names without the hash; <c>none</c> for production instructions.</summary>
    public string? Variant { get; set; }
    public string? Harness { get; set; }

    /// <summary><c>FeedbackResolvedVia</c> ids, and <c>none</c> for issues with none stored or no triage row.</summary>
    public string? ResolvedVia { get; set; }
    public string? Fingerprints { get; set; }
    public string? Search { get; set; }

    /// <summary>lastSeen (the default) | firstSeen | count | runs | models | subject.</summary>
    public string? Sort { get; set; }
}

/// <summary>The Overseer chat issue with the same fingerprint.</summary>
public sealed class GnollBenchFeedbackOverseerLinkDto
{
    public long IssueId { get; set; }
    public string Status { get; set; } = "new";
}

/// <summary>One GnollBench feedback issue as the admin Feedback sub-tab shows it.</summary>
public sealed class GnollBenchFeedbackIssueDto
{
    public string Fingerprint { get; set; } = "";
    public string Category { get; set; } = "";

    /// <summary>From the newest self-report.</summary>
    public string Subject { get; set; } = "";

    /// <summary>From the newest self-report.</summary>
    public string Summary { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int SelfReportCount { get; set; }
    public int RunCount { get; set; }
    public int ModelCount { get; set; }

    /// <summary>Model display labels, at most five, then <c>+N</c> for the rest.</summary>
    public List<string> Models { get; set; } = new();
    public List<string> Providers { get; set; } = new();

    /// <summary>Suite names, at most five, then <c>+N</c> for the rest.</summary>
    public List<string> Suites { get; set; } = new();
    public string Status { get; set; } = "new";    // new | acknowledged | resolved | regressed | dismissed
    public string? TriageNote { get; set; }
    public string? Resolution { get; set; }
    public string? FixedInVersion { get; set; }

    /// <summary><c>FeedbackResolvedVia</c> ids, in their order; empty when none.</summary>
    public List<string> ResolvedVia { get; set; } = new();
    public string? TriagedByUserName { get; set; } // null when the triager's account is gone
    public DateTime? TriagedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
    public GnollBenchFeedbackOverseerLinkDto? AlsoInOverseer { get; set; }
}

/// <summary>One self-report of a GnollBench feedback issue, with its run's facts.</summary>
public sealed class GnollBenchFeedbackOccurrenceDto
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public long RunId { get; set; }
    public long? SeriesId { get; set; }
    public long? SuiteId { get; set; }
    public string SuiteName { get; set; } = "";
    public long? QuestionId { get; set; }
    public string QuestionText { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ModelLabel { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Condition { get; set; } = "production"; // production | sourceOnly | closedBook
    public string? Variant { get; set; }                  // the stored stamp, name:hash12
    public string? VariantName { get; set; }
    public string Corpus { get; set; } = "live";
    public string? Harness { get; set; }
    public bool Accepted { get; set; }
    public string? Error { get; set; }
    public string? Category { get; set; }
    public string? Subject { get; set; }
    public string? Summary { get; set; }
    public string? Details { get; set; }
    public string? SearchedFor { get; set; }
    public string? ExpectedLocation { get; set; }
    public List<string> RelatedTools { get; set; } = new();
    public string? NoticedVia { get; set; }
    public string? SuspectedCause { get; set; }
    public string? SuspectedCauseNote { get; set; }
    public bool Truncated { get; set; }
}

public sealed class GnollBenchFeedbackIssuePageDto
{
    public int TotalCount { get; set; }
    public List<GnollBenchFeedbackIssueDto> Rows { get; set; } = new();
}

public sealed class GnollBenchFeedbackOccurrencePageDto
{
    public int TotalCount { get; set; }
    public List<GnollBenchFeedbackOccurrenceDto> Rows { get; set; } = new();
}

/// <summary>The matching fingerprints in sort order, at most 1,000, and whether the cap cut them.</summary>
public sealed class GnollBenchFeedbackIdsDto
{
    public List<string> Fingerprints { get; set; } = new();
    public int TotalCount { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>Issue counts per derived status over accepted self-reports and all dates.</summary>
public sealed class GnollBenchFeedbackCountsDto
{
    public int New { get; set; }
    public int Acknowledged { get; set; }
    public int Resolved { get; set; }
    public int Regressed { get; set; }
    public int Dismissed { get; set; }
}

/// <summary>A facet option named by a string value, with the number of issues it covers.</summary>
public sealed class GnollBenchFeedbackFacetValueDto
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>A facet option named by an id, with the number of issues it covers.</summary>
public sealed class GnollBenchFeedbackFacetEntityDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>The filter options present among the self-reports in a date range.</summary>
public sealed class GnollBenchFeedbackFacetsDto
{
    public List<GnollBenchFeedbackFacetValueDto> Categories { get; set; } = new();

    /// <summary>Value is the model id; label its display label.</summary>
    public List<GnollBenchFeedbackFacetValueDto> Models { get; set; } = new();
    public List<GnollBenchFeedbackFacetValueDto> Providers { get; set; } = new();
    public List<GnollBenchFeedbackFacetEntityDto> Runs { get; set; } = new();
    public List<GnollBenchFeedbackFacetEntityDto> Series { get; set; } = new();
    public List<GnollBenchFeedbackFacetEntityDto> Groups { get; set; } = new();

    /// <summary>Battery runs.</summary>
    public List<GnollBenchFeedbackFacetEntityDto> Batteries { get; set; } = new();

    /// <summary>Model batches.</summary>
    public List<GnollBenchFeedbackFacetEntityDto> Batches { get; set; } = new();
    public List<GnollBenchFeedbackFacetEntityDto> Suites { get; set; } = new();

    /// <summary>Named by the question text, cut to 200 characters.</summary>
    public List<GnollBenchFeedbackFacetEntityDto> Questions { get; set; } = new();
    public List<GnollBenchFeedbackFacetValueDto> Conditions { get; set; } = new();

    /// <summary>Stored corpus conditions, <c>live</c> first.</summary>
    public List<GnollBenchFeedbackFacetValueDto> Corpora { get; set; } = new();

    /// <summary>Variant names without the hash, <c>none</c> first.</summary>
    public List<GnollBenchFeedbackFacetValueDto> Variants { get; set; } = new();

    /// <summary>Harness versions in numeric order.</summary>
    public List<GnollBenchFeedbackFacetValueDto> Harnesses { get; set; } = new();

    /// <summary><c>FeedbackResolvedVia</c> ids in their order, then <c>none</c> last.</summary>
    public List<GnollBenchFeedbackFacetValueDto> ResolvedVia { get; set; } = new();
}

/// <summary>
/// A triage change. An absent (or null) note, resolution or fixed-in version keeps the stored value, and an empty
/// string clears it; an absent <see cref="ResolvedVia"/> keeps the stored list and an empty one clears it.
/// </summary>
public sealed class GnollBenchFeedbackStatusUpdateDto
{
    public string Status { get; set; } = "";
    public string? Note { get; set; }
    public string? Resolution { get; set; }
    public string? FixedInVersion { get; set; }

    /// <summary><c>FeedbackResolvedVia</c> ids.</summary>
    public List<string>? ResolvedVia { get; set; }
}
