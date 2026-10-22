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
}

public sealed class AdminFeedbackIssuePageDto
{
    public int TotalCount { get; set; }
    public List<AdminFeedbackIssueDto> Rows { get; set; } = new();
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

public sealed class AdminFeedbackStatusUpdateDto
{
    public string Status { get; set; } = "";
    public string? Note { get; set; }
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
