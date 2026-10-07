namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;

// Request and response bodies of the GnollBench chat consistency API (AdminChatConsistencyController).
// The analysis, timeline, run table, annotation and re-grade records themselves live in
// Overseer.Services.ChatConsistency.ChatConsistencyModels and are returned as they are.

/// <summary>The body of <c>PUT runs/{id}/anchor</c>.</summary>
public sealed class ChatConsistencyAnchorRequest
{
    /// <summary>True marks the run as the grader anchor; false unmarks it.</summary>
    public bool IsAnchor { get; set; }
}

/// <summary>A run's anchor mark after <c>PUT runs/{id}/anchor</c>.</summary>
public sealed class ChatConsistencyAnchorResponse
{
    public long RunId { get; set; }
    public bool IsAnchor { get; set; }
}

/// <summary>The body of <c>POST regrade/estimate</c>.</summary>
public sealed class ChatConsistencyRegradeEstimateRequest
{
    public List<long>? RunIds { get; set; }
    public long AssessorConfigId { get; set; }
}

/// <summary>
/// The body of <c>POST annotations</c>. <see cref="Kind"/> is read by name (any case) or by number;
/// a kind that is not defined is refused.
/// </summary>
public sealed class ChatConsistencyAnnotationRequest
{
    /// <summary>When the annotated event happened; a time without an offset is UTC.</summary>
    public DateTime AtUtc { get; set; }

    /// <summary>Null or empty applies the annotation to every provider; at most 64 characters.</summary>
    public string? Provider { get; set; }

    /// <summary>Null or empty applies the annotation to every model of the provider; at most 128 characters.</summary>
    public string? ModelId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChatConsistencyAnnotationKind Kind { get; set; }

    /// <summary>Required; at most 1,000 characters.</summary>
    public string? Text { get; set; }

    /// <summary>An absolute http or https URL of at most 512 characters, or null.</summary>
    public string? SourceUrl { get; set; }
}
