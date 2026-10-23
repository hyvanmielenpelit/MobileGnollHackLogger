namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MobileGnollHackLogger.Data;

/// <summary>One feedback issue as the admin Feedback sub-tab shows it, with aggregates over its retained occurrences.</summary>
public sealed class AdminFeedbackIssueDto
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Every occurrence ever recorded; purges and erasures do not lower it.</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>Occurrence rows that still exist.</summary>
    public int RetainedCount { get; set; }
    public int DistinctUserCount { get; set; }

    /// <summary>Distinct model ids of the retained occurrences, at most five, then <c>+N</c> for the rest.</summary>
    public List<string> ModelIds { get; set; } = new();

    /// <summary>Distinct providers of the retained occurrences, in ordinal order.</summary>
    public List<string> Providers { get; set; } = new();

    /// <summary>Retained occurrences per model source.</summary>
    public AdminFeedbackModelSourceCountsDto ModelSources { get; set; } = new();
    public int RegressionCount { get; set; }
    public DateTime? LastRegressionUtc { get; set; }
    public string? LatestExpectedLocation { get; set; }
    public List<string> LatestRelatedTools { get; set; } = new();
    public string? LatestSuspectedCause { get; set; }
    public string? LatestAppVersion { get; set; }
    public string Status { get; set; } = "new";    // new | acknowledged | resolved | regressed | dismissed
    public string? TriageNote { get; set; }
    public string? TriagedByUserName { get; set; } // null when the triager's account is gone
    public DateTime? TriagedUtc { get; set; }
    public string? Resolution { get; set; }
    public string? FixedInVersion { get; set; }

    /// <summary>
    /// Accepted GnollBench self-reports with the same fingerprint; null when there are none. The chat fingerprints
    /// the redacted arguments, so a subject the scrub changed is not linked.
    /// </summary>
    public AdminFeedbackGnollBenchLinkDto? AlsoInGnollBench { get; set; }
}

/// <summary>Retained occurrences of an issue by where the turn's model came from.</summary>
public sealed class AdminFeedbackModelSourceCountsDto
{
    public int System { get; set; }
    public int User { get; set; }
    public int Unknown { get; set; }
}

/// <summary>The GnollBench issue that shares an Overseer issue's fingerprint.</summary>
public sealed class AdminFeedbackGnollBenchLinkDto
{
    public int SelfReportCount { get; set; }

    /// <summary>The derived GnollBench status: new | acknowledged | resolved | regressed | dismissed.</summary>
    public string Status { get; set; } = "new";
}

/// <summary>One occurrence of a feedback issue.</summary>
public sealed class AdminFeedbackOccurrenceDto
{
    public long Id { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string Subject { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Details { get; set; }
    public string? SearchedFor { get; set; }
    public string? ExpectedLocation { get; set; }
    public List<string> RelatedTools { get; set; } = new();
    public string? NoticedVia { get; set; }
    public string? SuspectedCause { get; set; }
    public string? SuspectedCauseNote { get; set; }
    public bool Truncated { get; set; }
    public bool Redacted { get; set; }
    public long ChatSessionId { get; set; }
    public long? TurnUserMessageId { get; set; }
    public string? Provider { get; set; }
    public string? ModelId { get; set; }
    public string? AppVersion { get; set; }
    public string? UserName { get; set; }          // joined from AspNetUsers; null once erased

    /// <summary><c>system</c> or <c>user</c>; null when unknown.</summary>
    public string? ModelSource { get; set; }
    public long? SystemConfigurationId { get; set; }

    /// <summary>The configuration's display name, or <c>Configuration #id (deleted)</c>; null without a configuration.</summary>
    public string? SystemConfigurationName { get; set; }

    /// <summary>The reporting user's current groups, by name.</summary>
    public List<string> GroupNames { get; set; } = new();
}

public sealed class AdminFeedbackIssuePageDto
{
    public int TotalCount { get; set; }
    public List<AdminFeedbackIssueDto> Rows { get; set; } = new();
}

/// <summary>Every matching issue id, in the list's order, up to the cap.</summary>
public sealed class AdminFeedbackIssueIdsDto
{
    public List<long> Ids { get; set; } = new();
    public int TotalCount { get; set; }

    /// <summary>True when <see cref="TotalCount"/> exceeds the ids returned.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// The issue filters of the Feedback sub-tab as query values. Every list is comma-separated; an empty value is no
/// filter. The occurrence-level filters (model through tool) match an issue when one retained occurrence matches
/// all of them together.
/// </summary>
public sealed class AdminFeedbackFilterQuery
{
    public DateTime? StartDate { get; set; }

    /// <summary>Inclusive: the whole day.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Wire statuses.</summary>
    public string? Status { get; set; }

    /// <summary>Category wire values.</summary>
    public string? Category { get; set; }
    public string? ModelId { get; set; }
    public string? Provider { get; set; }

    /// <summary>Identity user ids.</summary>
    public string? User { get; set; }

    /// <summary>Group ids, and <c>none</c> for occurrences whose user is in no group.</summary>
    public string? Group { get; set; }

    /// <summary><c>system</c>, <c>user</c> or <c>unknown</c>.</summary>
    public string? Source { get; set; }

    /// <summary>System AI configuration ids.</summary>
    public string? Config { get; set; }

    /// <summary>App versions.</summary>
    public string? Version { get; set; }

    /// <summary>Related tool names, compared ignoring case.</summary>
    public string? Tool { get; set; }

    /// <summary><c>with</c> or <c>without</c> a written resolution.</summary>
    public string? Resolution { get; set; }

    /// <summary>Issue ids.</summary>
    public string? Ids { get; set; }

    /// <summary>Subject, summary, and the occurrences' details, searched-for and expected location, ignoring case.</summary>
    public string? Search { get; set; }
}

/// <summary>A plain facet option: the value to filter by and how many issues in the range have it.</summary>
public sealed class AdminFeedbackFacetValueDto
{
    public string Value { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>A facet option that names an entity: the id to filter by, its display name and the issue count.</summary>
public sealed class AdminFeedbackFacetEntityDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>The facet options present among the issues in a date range, each with a count of issues.</summary>
public sealed class AdminFeedbackFacetsDto
{
    public List<AdminFeedbackFacetValueDto> Categories { get; set; } = new();
    public List<AdminFeedbackFacetValueDto> Models { get; set; } = new();
    public List<AdminFeedbackFacetValueDto> Providers { get; set; } = new();
    public List<AdminFeedbackFacetEntityDto> Users { get; set; } = new();

    /// <summary>Groups by id, then <c>none</c> (<c>No group</c>) last.</summary>
    public List<AdminFeedbackFacetEntityDto> Groups { get; set; } = new();

    /// <summary><c>system</c>, <c>user</c>, <c>unknown</c>, in that order.</summary>
    public List<AdminFeedbackFacetValueDto> Sources { get; set; } = new();
    public List<AdminFeedbackFacetEntityDto> Configs { get; set; } = new();

    /// <summary>Newest version first.</summary>
    public List<AdminFeedbackFacetValueDto> Versions { get; set; } = new();
    public List<AdminFeedbackFacetValueDto> Tools { get; set; } = new();
}

public sealed class AdminFeedbackOccurrencePageDto
{
    public int TotalCount { get; set; }
    public List<AdminFeedbackOccurrenceDto> Rows { get; set; } = new();
}

/// <summary>Issue counts per status over all dates.</summary>
public sealed class AdminFeedbackCountsDto
{
    public int New { get; set; }
    public int Acknowledged { get; set; }
    public int Resolved { get; set; }
    public int Regressed { get; set; }
    public int Dismissed { get; set; }
}

/// <summary>One feedback switch. <see cref="Seeded"/> is true while no administrator has set it.</summary>
public sealed class AdminFeedbackSwitchDto
{
    public bool Enabled { get; set; }
    public DateTime? SetAtUtc { get; set; }
    public string? SetByUserName { get; set; }
    public bool Seeded { get; set; }
}

/// <summary>The feedback switch, as the card shows it.</summary>
public sealed class AdminFeedbackSwitchesDto
{
    public AdminFeedbackSwitchDto Chat { get; set; } = new();
}

public sealed class AdminFeedbackSwitchUpdateDto
{
    public bool Enabled { get; set; }
}

/// <summary>A triage. For each text field, absent (null) keeps the stored value and empty or blank clears it.</summary>
public sealed class AdminFeedbackStatusUpdateDto
{
    public string Status { get; set; } = "";
    public string? Note { get; set; }
    public string? Resolution { get; set; }
    public string? FixedInVersion { get; set; }
}

/// <summary>The lowercase wire values of <see cref="OverseerFeedbackStatus"/>.</summary>
public static class AdminFeedbackStatuses
{
    public const string New = "new";
    public const string Acknowledged = "acknowledged";
    public const string Resolved = "resolved";
    public const string Regressed = "regressed";
    public const string Dismissed = "dismissed";

    public static string ToWire(OverseerFeedbackStatus status) => status switch
    {
        OverseerFeedbackStatus.Acknowledged => Acknowledged,
        OverseerFeedbackStatus.Resolved => Resolved,
        OverseerFeedbackStatus.Regressed => Regressed,
        OverseerFeedbackStatus.Dismissed => Dismissed,
        _ => New
    };

    /// <summary>Parses a wire value, ignoring case and surrounding whitespace.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out OverseerFeedbackStatus status)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case New: status = OverseerFeedbackStatus.New; return true;
            case Acknowledged: status = OverseerFeedbackStatus.Acknowledged; return true;
            case Resolved: status = OverseerFeedbackStatus.Resolved; return true;
            case Regressed: status = OverseerFeedbackStatus.Regressed; return true;
            case Dismissed: status = OverseerFeedbackStatus.Dismissed; return true;
            default: status = OverseerFeedbackStatus.New; return false;
        }
    }
}
